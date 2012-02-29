using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using RedisQueue.Net.Clients;
using RedisQueue.Net.Clients.Entities;
using RedisQueue.Net.Clients.Exceptions;
using RedisQueue.Net.ServiceProvider.Properties;
using log4net;

namespace RedisQueue.Net.ServiceProvider
{
	public class RedisMonitor
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (RedisMonitor));

		protected internal virtual bool Running { get; set; }
		public Thread MonitorThread { get; set; }
		protected internal QueueClient Client { get; set; }
		public virtual Performer Performer { get; set; }

		public RedisMonitor()
		{
			InitializeWorker();
			InitializeMonitorThread();
		}

		public virtual void Start()
		{
			//SubscribeToQueue();

			// Start the execution loop.
			Running = true;
			MonitorThread.Start();
		}

		protected void InitializeClient() { Client = new QueueClient(); }
		protected void InitializeMonitorThread() { MonitorThread = new Thread(Run); }

		private void SubscribeToQueue()
		{
			throw new System.NotImplementedException();
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

		public virtual void Stop()
		{
			Running = false;
		}

		protected internal virtual void Run()
		{
			while (Running)
			{
				try
				{
					// Try connecting to Redis by creating a new client if we don't have any.
					if (Client == null) InitializeClient();

					ProcessPendingTasks();
				}
				catch (IOException exception)
				{
					if (exception.InnerException is SocketException)
					{
						Log.Error("Could not connect to Redis. Will sleep and attempt again in a while.", exception);

						if (Client != null)
						{
							Client.Dispose();
							Client = null;
						}
					}
					else throw;
				}
				
				Thread.Sleep(Settings.Default.MonitorSleepIntervalInMilliseconds);
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
				catch { }

				var message =
					"The performer raised an exception: "
						+ exception.Message
						+ "\n"
						+ exception.StackTrace;

				Client.Fail(message);
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
					Client.Succeed();
					Log.Info("Task succeeded.");
					break;

				case Outcome.Failure:
					Client.Fail(result.Reason);
					Log.InfoFormat("Task failed. Reason: {0}", result.Reason);

					if (result.Data is Exception)
						Log.Error("Exception associated with task failure:", result.Data as Exception);

					if (result.Data is string)
						Log.InfoFormat("Additional Information: {0}", result.Data);

					break;

				case Outcome.CriticalFailure:
					Client.CriticalFail(result.Reason);
					Log.InfoFormat("Task failed critically. Reason: {0}", result.Reason);

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
			if (Client.CurrentTask != null)
			{
				examinedTasks.Add(Client.CurrentTask.Identifier);
				ProcessTask(Client.CurrentTask);
			}

			while(anyNewTasks && Running)
			{
				anyNewTasks = false;
				var taskCount = Client.PendingTasks(Settings.Default.Queue).Count;
				for (var index = 0; index < taskCount && Running; index++)
				{
					TaskMessage task;

					try { task = Client.Reserve(Settings.Default.Queue); }
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
	}
}