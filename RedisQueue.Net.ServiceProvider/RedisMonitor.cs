using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using RedisQueue.Net.Clients;
using RedisQueue.Net.Clients.Entities;
using RedisQueue.Net.Clients.Exceptions;
using RedisQueue.Net.ServiceProvider.Properties;
using ServiceStack.Redis;
using log4net;

namespace RedisQueue.Net.ServiceProvider
{
	public class RedisMonitor
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (RedisMonitor));

		protected internal virtual bool Running { get; set; }
		public Thread MonitorThread { get; set; }
		public Thread SubscriberThread { get; set; }


		protected internal QueueClient SubscriptionClient { get; set; }
		protected internal QueueClient MonitorClient { get; set; }

		public virtual Performer Performer { get; set; }

		protected internal bool Working { get; set; }
		protected internal bool Nudged { get; set; }

		protected internal readonly object ThreadLock = new object();

		protected internal IRedisSubscription Subscription { get; set; }

		protected internal string RedisHost { get; set; }
		protected internal int RedisPort { get; set; }

		#region Ctors

		public RedisMonitor()
		{
			InitializeWorker();
			InitializeThreads();
			InitializeMonitorClient();
			InitializeSubscriptionClient();
		}

		public RedisMonitor(string host, int port)
		{
			RedisHost = host;
			RedisPort = port;

			InitializeWorker();
			InitializeThreads();
			InitializeMonitorClient();
			InitializeSubscriptionClient();
		}

		#endregion

		#region Initialization

		protected void InitializeMonitorClient()
		{
			if (!string.IsNullOrWhiteSpace(RedisHost) && RedisPort > 0)
			{
				MonitorClient = new QueueClient(RedisHost, RedisPort, true);
				return;
			}

			MonitorClient = new QueueClient();
		}
		
		protected void InitializeSubscriptionClient()
		{
			if (!string.IsNullOrWhiteSpace(RedisHost) && RedisPort > 0)
			{
				SubscriptionClient = new QueueClient(RedisHost, RedisPort, true);
				return;
			}

			SubscriptionClient = new QueueClient();
		}

		protected void InitializeThreads()
		{
			MonitorThread = new Thread(Run);
			SubscriberThread = new Thread(DoSubscribe);
		}

		protected virtual void InitializeWorker()
		{
			var path = ResolvePath(Settings.Default.WorkerAssemblyPath);

			Performer = Assembly
				.LoadFile(path)
				.GetTypes()
				.Where(x => x.IsSubclassOf(typeof (Performer)))
				.Select(Activator.CreateInstance)
				.Cast<Performer>()
				.FirstOrDefault();
		}

		#endregion

		#region Messaging

		protected virtual void SubscribeToQueue()
		{
			Subscription = SubscriptionClient.GetSubscription();
			Subscription.OnMessage = (queue, message) =>
			{
				Log.Debug("Message received: " + message);
				Nudged = (QueueSystemMessages.TaskAvailable.ToString() == message);
				if (Nudged) MonitorThread.Interrupt();
			};

			SubscriberThread.Start();
		}

		protected virtual void UnSubscribeFromQueue()
		{
			SubscriberThread.Interrupt();
		}

		protected virtual void DoSubscribe()
		{
			IAsyncResult handle = null;

			try
			{
				handle = new Action(BlockSubscribe).BeginInvoke(null, null);
				handle.AsyncWaitHandle.WaitOne();
			}
			catch(ThreadInterruptedException)
			{
				// will throw an ObjectDisposedException at the point of invocation (the BeginInvoke() above).
				if (handle == null) return;
				handle.AsyncWaitHandle.Close(); 
				Log.Debug("Stopped subscription to queue channel.");
			}
			catch(ObjectDisposedException) {}
		}

		protected virtual void BlockSubscribe()
		{
			try
			{
				var queueName = new QueueName(Settings.Default.Queue);
				Subscription.SubscribeToChannels(queueName.ChannelName);
			}

			catch (Exception exception)
			{
				if (!(exception is RedisException) && !(exception is IOException)) throw;

				Log.Error("IOException while listening for channel messages. Will subscribe again in a second.", exception);
				Thread.Sleep(1000);
				BlockSubscribe();
			}
		}

		#endregion

		#region Monitor

		public virtual void Start()
		{
			SubscribeToQueue();
			MonitorThread.Start();
			Running = true;
		}

		public virtual void Stop()
		{
			UnSubscribeFromQueue();

			Running = false;
			MonitorThread.Interrupt();
		}

		protected internal virtual void Run()
		{
			while (true)
			{
				try
				{
					try { ProcessPendingTasks(); }
					catch (Exception exception)
					{
						if (!(exception is RedisException) && !(exception is IOException)) throw;
						Log.Error("Could not connect to Redis. Will sleep and attempt again in a while.", exception);
					}

					Thread.Sleep(Settings.Default.MonitorSleepIntervalInMilliseconds);
				}
				catch (ThreadInterruptedException)
				{
					Log.Debug("Nudged awake. Resuming.");
					if (!Running) return;
				}
			}
		}

		/// <summary>
		/// Processes a single task and updates the queue accordingly.
		/// </summary>
		/// <param name="task"></param>
		protected internal void ProcessTask(TaskMessage task)
		{
			// Get the state of the task into the performer.
			Performer.TaskStorage = task.Storage;

			// Let the performer have a go with the task.
			try { Performer.Perform(task.Parameters); }
			catch (Exception exception)
			{
				// The performer has raised an exception while processing 
				// the task. We do what we can to preserve the task,
				// and set it back as failed.
				try { task.Storage = Performer.TaskStorage; }
				catch {}

				var message =
					"The performer raised an exception: "
					+ exception.Message
					+ "\n"
					+ exception.StackTrace;

				MonitorClient.Fail(message);
				Log.Error(message);

				return;
			}

			// Collect the task's state and save it back to the task.
			task.Storage = Performer.TaskStorage;

			// Collect the result of the perfomer's work and act on it.
			var result = Performer.Status;

			switch (result.Outcome)
			{
				case Outcome.Success:
					MonitorClient.Succeed();
					Log.Info("Task succeeded.");
					break;

				case Outcome.Failure:
					MonitorClient.Fail(result.Reason);
					Log.InfoFormat("Task failed. Reason: {0}", result.Reason);

					if (result.Data is Exception)
						Log.Error("Exception associated with task failure:", result.Data as Exception);

					if (result.Data is string)
						Log.InfoFormat("Additional Information: {0}", result.Data);

					break;
				case Outcome.CriticalFailure:
					MonitorClient.CriticalFail(result.Reason);
					Log.InfoFormat("Task failed critically. Reason: {0}", result.Reason);

					if (result.Data is string)
						Log.InfoFormat("Additional Information: {0}", result.Data);

					break;

                case Outcome.Defer:
                    MonitorClient.Defer(result.Reason);
                    Log.InfoFormat("Task deferred. Reason: {0}", result.Reason);

                    if (result.Data is string)
                        Log.InfoFormat("Additional Information: {0}", result.Data);
			        break;
			}
		}

		/// <summary>
		/// Processes all pending tasks that can be found in the queue.
		/// </summary>
		protected internal virtual void ProcessPendingTasks()
		{
			var examinedTasks = new HashSet<Guid>();
			var anyNewTasks = true;

			// if a task had been preserved from the previous iteration,
			// process it before moving forward.
			if (MonitorClient.CurrentTask != null)
			{
				examinedTasks.Add(MonitorClient.CurrentTask.Identifier);
				ProcessTask(MonitorClient.CurrentTask);
			}

			while(anyNewTasks)
			{
				anyNewTasks = false;
				var taskCount = MonitorClient.PendingTasks(Settings.Default.Queue).Count;
				for (var index = 0; index < taskCount; index++)
				{
					TaskMessage task;

					try { task = MonitorClient.Reserve(Settings.Default.Queue); }
					catch (QueueIsEmptyException)
					{
						Log.DebugFormat("No tasks in queue [{0}]", new QueueName(Settings.Default.Queue).NameWhenPending);
						return;
					}
					
					if (examinedTasks.Contains(task.Identifier)) continue;
					
					anyNewTasks = true;
					ProcessTask(task);
					examinedTasks.Add(task.Identifier);
				}
			}
		}

		#endregion

		#region Utils

		/// <summary>
		/// Resolves the path to an assembly, at first trying the path from 
		/// the current directory, then attempting to get the path from the current AppDomain's 
		/// base directory.
		/// </summary>
		/// <param name="path"></param>
		/// <returns>The path or an empty string, if the path cannot be located.</returns>
		internal static string ResolvePath(string path)
		{
			if (File.Exists(path)) return Path.GetFullPath(path);
			var supposedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
			return File.Exists(supposedPath) ? Path.GetFullPath(supposedPath) : string.Empty;
		}

		#endregion
	}
}