using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RedisQueue.Net.Clients.Entities;
using RedisQueue.Net.Clients.Exceptions;
using RedisQueue.Net.Clients.Properties;
using ServiceStack.Redis;
using ServiceStack.Redis.Generic;
using ServiceStack.Text;
using log4net;

namespace RedisQueue.Net.Clients
{
	public class QueueClient : IQueueClient
	{
		/// <summary>
		/// Log4Net logger.
		/// </summary>
		private static readonly ILog Log = LogManager.GetLogger(typeof(QueueClient));

		/// <summary>
		/// Typewise Redis client. Used in Queue operations.
		/// </summary>
		protected IRedisTypedClient<TaskMessage> TypedClient { get; set; }

		/// <summary>
		/// Generic Redis Client. Used in message subscriptions.
		/// </summary>
		protected RedisClient GenericClient { get; set; }

		/// <summary>
		/// The internal state of the client. Either Ready or TaskReserved.
		/// If Ready, can reserve and enqueue tasks.
		/// If TaskReserved, can only enqueue tasks.
		/// </summary>
		protected RedisQueueState State { get; set; }

		/// <summary>
		/// The task currently reserved, if any.
		/// </summary>
		public virtual TaskMessage CurrentTask { get; protected internal set; }

		/// <summary>
		/// The host name where Redis can be located.
		/// </summary>
		public virtual string RedisHost { get; set; }

		/// <summary>
		/// The port at which Redis listens.
		/// </summary>
		public virtual int RedisPort { get; set; }

		/// <summary>
		/// Determines whether caching is enabled.
		/// </summary>
		protected bool LocalCachingEnabled { get; private set; }

		public QueueClient() : this(Settings.Default.RedisHost, Settings.Default.RedisPort, true) {}
		public QueueClient(string host = "127.0.0.1", int port = 6379, bool enableCaching = true)
		{
			RedisHost = host;
			RedisPort = port;

			GenericClient = new RedisClient(RedisHost, RedisPort);
			TypedClient = GenericClient.GetTypedClient<TaskMessage>();
			
			Log.Info("Connected to Redis.");
			Log.DebugFormat("Connection Properties: {0}:{1}", Settings.Default.RedisHost, Settings.Default.RedisPort);
			
			LocalCachingEnabled = enableCaching;
			if (LocalCachingEnabled)
			{
				Log.Info("Caching 's enabled. Cache location: " + Settings.Default.LocalCache);
				CheckCacheAndRetrieveState();
			}
		}

		#region Queuing API
		/// <summary>
		/// Adds a task to the queue specified in the task itself and sends a message to
		/// the associated channel, notifying any listeners.
		/// </summary>
		/// <param name="t"></param>
		public void Enqueue(TaskMessage t)
		{
			if (string.IsNullOrEmpty(t.Queue))
				throw new NoQueueSpecifiedException(
					"TaskMessage.Queue is empty or null. Cannot append task to queue.");

			var queue = new QueueName(t.Queue);

			TypedClient.Lists[queue.NameWhenPending].Add(t);
			SendMessage(QueueSystemMessages.TaskAvailable.ToString(), queue);

			Log.Info("New task in [" + queue.NameWhenPending + "]");
			Log.DebugFormat("Task Parameters: {0}", t.Parameters);
		}

		/// <summary>
		/// Obtains the first available task from the specified queue, and changes the client's
		/// state into TaskReserved. In that state, no new tasks can be reserved until the 
		/// task's outcome is evident.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public TaskMessage Reserve(QueueName queue)
		{
			if (State == RedisQueueState.TaskReserved)
				throw new TaskAlreadyReservedException("Cannot reserve multiple tasks at once.");

			if (string.IsNullOrEmpty(queue.ToString()))
				throw new NoQueueSpecifiedException(
					"Parameter <queue> is empty or null. Cannot retrieve task for no queue.");

			if (TypedClient.Lists[queue.NameWhenPending].Count == 0)
				throw new QueueIsEmptyException("No tasks available in specified queue.");

			CurrentTask = TypedClient.Lists[queue.NameWhenPending].RemoveStart();
			State = RedisQueueState.TaskReserved;

			Log.Info("Reserved task from [" + queue.NameWhenPending + "]");
			Log.DebugFormat("Task Parameters: {0}", CurrentTask.Parameters);

			CacheCurrentTask();

			return CurrentTask;
		}

		/// <summary>
		/// Obtains the first available task from the specified queue, and changes the client's
		/// state into TaskReserved. In that state, no new tasks can be reserved until the 
		/// task's outcome is evident.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public TaskMessage Reserve(string queue) { return Reserve(new QueueName(queue)); }

		/// <summary>
		/// Disposable impl. Handles cleanup and marks any uncompleted tasks as failed.
		/// </summary>
		public void Dispose()
		{
			// the consumer did not assert the task was succesfully completed,
			// or something sinister has happened. Add the task to the failed tasks list.
			if (State == RedisQueueState.TaskReserved && !LocalCachingEnabled)
				Fail("RedisClient disposed prior to the task being completed.");

			TypedClient.Dispose();
			GenericClient.Dispose();

			Log.Info("Disconnected from Redis.");
			Log.DebugFormat("Client state on disconnect: {0}", State);
		}

		/// <summary>
		/// Marks the currently reserved task as failed.
		/// </summary>
		/// <param name="reason"></param>
		/// <exception cref="NoTaskReservedException">No task has been reserved. Future failure not yet 
		/// supported.</exception>
		/// <exception cref="InvalidStateException">CurrentTask is null. Something's seriously off.</exception>
		public virtual void Fail(string reason)
		{
			if (State != RedisQueueState.TaskReserved)
				throw new NoTaskReservedException("No task has been reserved. Future failure not yet supported.");

			if (CurrentTask == null)
				throw new InvalidStateException("CurrentTask is null. Something's seriously off.");

			CurrentTask.Status = TaskStatus.Failed;
			CurrentTask.Reason = reason;
			CurrentTask.UpdatedOn = DateTime.Now;

			var queue = new QueueName(CurrentTask.Queue);
			var targetQueue = queue.NameWhenFailed;

			// If we support task cycling and the task should indeed be recycled, 
			// place it to the end of the pending queue again.
			if (Settings.Default.TaskRecycling)
				if (Settings.Default.MaxTaskRetries == 0 || CurrentTask.Retries < Settings.Default.MaxTaskRetries)
				{
					targetQueue = queue.NameWhenPending;

					// If we support cycling, and we have a limit to how many times tasks
					// can be recycled, keep track of the number of retries. Do not remove this
					// lightly, since it can produce arithmetic overflows (<TaskMessage>.Retries is
					// an int, after all).
					if (CurrentTask.Retries < Settings.Default.MaxTaskRetries) CurrentTask.Retries++;
				}

			TypedClient.Lists[targetQueue].Add(CurrentTask);
			PurgeCache();

			Log.Info("A task has failed. Moving to [" + targetQueue + "].");
			Log.DebugFormat("Task Parameters: {0}", CurrentTask.Parameters);

			State = RedisQueueState.Ready;
		}

		/// <summary>
		/// Marks the current task as failed, and regardless of the cycling setting, places it 
		/// in the :failed queue.
		/// </summary>
		/// <param name="reason"></param>
		public virtual void CriticalFail(string reason)
		{
			if (State != RedisQueueState.TaskReserved)
				throw new NoTaskReservedException("No task has been reserved. Future failure not yet supported.");

			if (CurrentTask == null)
				throw new InvalidStateException("CurrentTask is null. Something's seriously off.");

			CurrentTask.Status = TaskStatus.Failed;
			CurrentTask.Reason = reason;
			CurrentTask.UpdatedOn = DateTime.Now;

			var targetQueue = new QueueName(CurrentTask.Queue).NameWhenFailed;
			TypedClient.Lists[targetQueue].Add(CurrentTask);
			PurgeCache();

			Log.Info("A task has failed. Moving to [" + targetQueue + "].");
			Log.DebugFormat("Task Parameters: {0}", CurrentTask.Parameters);

			State = RedisQueueState.Ready;
		}

		/// <summary>
		/// Marks the currently reserved task as succeeded.
		/// </summary>
		/// <exception cref="InvalidStateException">CurrentTask is null. Something's seriously off.</exception>
		/// <exception cref="NoTaskReservedException">No task has been reserved. Future success not yet 
		/// supported.</exception>
		public virtual void Succeed()
		{
			if (State != RedisQueueState.TaskReserved)
				throw new NoTaskReservedException(
					"No task has been reserved. Future success not yet supported.");

			if (CurrentTask == null)
				throw new InvalidStateException("CurrentTask is null. Something's seriously off.");

			CurrentTask.Status = TaskStatus.Succeeded;
			CurrentTask.UpdatedOn = DateTime.Now;

			if (!Settings.Default.PurgeSuccessfulTasks)
			{
				var queue = new QueueName(CurrentTask.Queue);
				TypedClient.Lists[queue.NameWhenSucceeded].Add(CurrentTask);
				Log.Info("A task has succeeded. Moving to [" + queue.NameWhenSucceeded + "].");
			}
			else
			{
				Log.Info("A task has succeeded and will be dropped.");
			}

			PurgeCache();
			Log.DebugFormat("Task Parameters: {0}", CurrentTask.Parameters);
			State = RedisQueueState.Ready;
		}

		/// <summary>
		/// Returns all pending tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public IList<TaskMessage> PendingTasks(QueueName queue)
		{
			if (string.IsNullOrEmpty(queue.ToString()))
				throw new NoQueueSpecifiedException(
					"Parameter <queue> is empty or null. Cannot retrieve task for no queue.");

			return TypedClient.Lists[queue.NameWhenPending].ToList();
		}

		/// <summary>
		/// Returns all pending tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public IList<TaskMessage> PendingTasks(string queue)
		{
			return PendingTasks(new QueueName(queue));
		}

		/// <summary>
		/// Returns all failed tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public IList<TaskMessage> FailedTasks(QueueName queue)
		{
			if (string.IsNullOrEmpty(queue.ToString()))
				throw new NoQueueSpecifiedException(
					"Parameter <queue> is empty or null. Cannot retrieve task for no queue.");

			return TypedClient.Lists[queue.NameWhenFailed].ToList();
		}

		/// <summary>
		/// Returns all failed tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public IList<TaskMessage> FailedTasks(string queue)
		{
			return FailedTasks(new QueueName(queue));
		}

		/// <summary>
		/// Returns all tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public IList<TaskMessage> AllTasks(QueueName queue)
		{
			if (string.IsNullOrEmpty(queue.ToString()))
				throw new NoQueueSpecifiedException(
					"Parameter <queue> is empty or null. Cannot retrieve task for no queue.");

			return TypedClient.Lists[queue.NameWhenFailed]
				.Union(TypedClient.Lists[queue.NameWhenSucceeded])
				.Union(TypedClient.Lists[queue.NameWhenPending])
				.ToList();
		}

		/// <summary>
		/// Returns all tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public IList<TaskMessage> AllTasks(string queue)
		{
			return AllTasks(new QueueName(queue));
		}

		/// <summary>
		/// Returns all succeeded tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public IList<TaskMessage> SucceededTasks(QueueName queue)
		{
			if (string.IsNullOrEmpty(queue.ToString()))
				throw new NoQueueSpecifiedException(
					"Parameter <queue> is empty or null. Cannot retrieve task for no queue.");

			return TypedClient.Lists[queue.NameWhenSucceeded];
		}

		/// <summary>
		/// Returns all succeeded tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		public IList<TaskMessage> SucceededTasks(string queue)
		{
			return SucceededTasks(new QueueName(queue));
		}

		/// <summary>
		/// Returns a list of all queues found.
		/// </summary>
		/// <returns></returns>
		public IList<string> AllQueues()
		{
			var allKeys = TypedClient.GetAllKeys().Where(x => x.StartsWith("queue:"));

			var queues = new HashSet<string>();

			foreach (var key in allKeys)
				queues.Add(key.Split(':')[1]);

			return queues.ToList();
		}

		/// <summary>
		/// Removes a task with a specific task identifier.
		/// </summary>
		/// <param name="task"></param>
		public void RemoveTask(TaskMessage task)
		{
			var queueName = new QueueName(task.Queue);
			removeTask(queueName.NameWhenPending, task);
			removeTask(queueName.NameWhenFailed, task);
			removeTask(queueName.NameWhenSucceeded, task);
		}

		private void removeTask(string queue, TaskMessage task)
		{
			var results = new List<TaskMessage>();
			TaskMessage t;
			while ((t = TypedClient.Lists[queue].RemoveStart()) != null)
			{
				if (t.Equals(task)) continue;
				results.Add(t);
			}

			if (results.Count > 0)
				TypedClient.Lists[queue].AddRange(results);
		}
		#endregion

		#region Channel API
		/// <summary>
		/// Returns an empty channel subscription.
		/// </summary>
		/// <returns></returns>
		public IRedisSubscription GetSubscription()
		{
			return GenericClient.CreateSubscription();
		}

		/// <summary>
		/// Sends a message to the channel associated with a specific queue.
		/// </summary>
		/// <param name="message"></param>
		/// <param name="queue"></param>
		public void SendMessage(string message, QueueName queue)
		{
			GenericClient.PublishMessage(queue.ChannelName, message);
		}

		/// <summary>
		/// Sends a message to the channel associated with a specific queue.
		/// </summary>
		/// <param name="message"></param>
		/// <param name="queue"></param>
		public void SendMessage(string message, string queue)
		{
			SendMessage(message, new QueueName(queue));
		}
		#endregion

		#region Local caching
		protected void CacheCurrentTask()
		{
			if (LocalCachingEnabled)
			{
				var serializer = new JsonSerializer<TaskMessage>();
				File.WriteAllText(Settings.Default.LocalCache, serializer.SerializeToString(CurrentTask));
			}
		}

		protected void PurgeCache()
		{
			if (LocalCachingEnabled && File.Exists(Settings.Default.LocalCache))
				File.Delete(Settings.Default.LocalCache);
		}

		protected void CheckCacheAndRetrieveState()
		{
			if (LocalCachingEnabled && File.Exists(Settings.Default.LocalCache))
			{
				var text = File.ReadAllText(Settings.Default.LocalCache);
				var serializer = new JsonSerializer<TaskMessage>();
				CurrentTask = serializer.DeserializeFromString(text);
				State = RedisQueueState.TaskReserved;
			}
		}
		#endregion
	}
}