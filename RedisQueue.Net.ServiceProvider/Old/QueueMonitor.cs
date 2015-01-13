using System;
using System.IO;
using System.Threading;
using RedisQueue.Net.Clients;
using RedisQueue.Net.Clients.Entities;
using RedisQueue.Net.Clients.Exceptions;
using RedisQueue.Net.ServiceProvider.Properties;
using log4net;

namespace RedisQueue.Net.ServiceProvider.Old
{
	public class QueueMonitor
	{
		/// <summary>
		/// Log4Net logger.
		/// </summary>
		private static readonly ILog Log = LogManager.GetLogger(typeof(QueueMonitor));

		private bool KeepWorking;

		protected Thread MonitorThread { get; set; }
		protected AppDomain Domain { get; set; }
		protected Performer Performer { get; set; }
		protected QueueClient Queue { get; set; }
		public bool Waiting { get; private set; }
		protected bool NewTasksArrived { get; set; }

		/// <summary>
		/// Lets the main process know that more tasks are available.
		/// </summary>
		public void SignalNewTasksArrived() { NewTasksArrived = true; }

		/// <summary>
		/// Ctor. Requires a {RedisQueueClient} instance.
		/// </summary>
		/// <param name="queue"></param>
		public QueueMonitor(QueueClient queue)
		{
			Queue = queue;	
		}

		/// <summary>
		/// Ctor. Creates and consumes a new {RedisQueueClient} instance.
		/// </summary>
		public QueueMonitor()
		{
			Queue = new QueueClient();
		}

		/// <summary>
		/// The main work method, signalling the monitor to start observing the queue defined 
		/// in configuration.
		/// </summary>
		public void Start()
		{
			try
			{
				if (MonitorThread != null
						&& (MonitorThread.ThreadState == ThreadState.Running
							|| MonitorThread.ThreadState == ThreadState.WaitSleepJoin)) return;

				MonitorThread = new Thread(Run);
				KeepWorking = true;
				MonitorThread.Start();

				Log.Info("Started monitor thread.");
			}
			catch (Exception exception)
			{
				Log.Fatal("Failed to start monitor thread.", exception);
			}
		}

		public void Stop()
		{
			try
			{
				lock(this)
				{
					// Set the flag, telling the thread it's gotta wrap up.
					KeepWorking = false;

					// In case it 's waiting, signal it to stop.
					Monitor.Pulse(this);
				}

				// Join it for 20 millis.
				MonitorThread.Join(20);

				// if it's not done by now, abort it.
				MonitorThread.Abort();

                // fail the current task.
			    if (Queue.CurrentTask != null)
			    {
			        Queue.Fail("Service is stopping.");
			    }

				// Attempt to release the application domain, if it 's there.
				releaseAppDomain();

				Log.Info("Stopped monitor thread.");
				Log.DebugFormat("Thread Identifier: {0}", MonitorThread.Name);

				MonitorThread = null;
			}
			catch (Exception exception)
			{
				Log.Error("Failed to stop monitor thread.", exception);
			}

		}

		/// <summary>
		/// The main monitor run method. What it does is try to reserve a task and dispatch it in a separate 
		/// application domain. A couple of notes follow:
		/// 
		/// AppDomain load / unload policy
		/// 
		/// The current policy is pretty straightforward. As long as the monitor 
		/// finds tasks in the queue, it will re-use the application domain. As soon as the queue is empty, 
		/// it will attempt to unload the application domain.
		/// 
		/// Stuck tasks
		/// 
		/// ...will currently hang the service. Probable TODO: Monitor the AppDomain and kill / fail stuck tasks.
		/// </summary>
		protected void Run()
		{
			lock(this)
			{
				while (KeepWorking)
				{
					// attempt to get a task.
					try
					{
						// Get the current task count and keep an index to avoid
						// re-iterating any failed tasks that may be enqueued.
						var taskCount = Queue.PendingTasks(Settings.Default.Queue).Count;

						for (var idx = 0; idx < taskCount; idx++)
						{
							var task = Queue.Reserve(Settings.Default.Queue);
							Log.Info("Reserved task from queue [queue:" + Settings.Default.Queue + "]");
							Log.DebugFormat("Task Parameters: {0}", task.Parameters);
							Log.DebugFormat("Task Index: {0}", idx);

							// if we have a task, then we need an AppDomain to work on. Create it if it's
							// not already there and then create the worker interface on it.
							if (Domain == null) initAppDomain();

							// Set the task's internal storage.
							Performer.TaskStorage = task.Storage;

							// Dispatch the task and collect its status.
							Performer.Perform(task.Parameters);
							var taskResult = Performer.Status;

							// Collect the task's internal storage.
							task.Storage = Performer.TaskStorage;

							// handle the different outcomes available.
							switch (taskResult.Outcome)
							{
								case Outcome.Success:
									Queue.Succeed();
									Log.Info("Task succeeded.");
									break;

								case Outcome.Failure:
									Queue.Fail(taskResult.Reason);
									Log.InfoFormat("Task failed. Reason: {0}", taskResult.Reason);

                                    if (taskResult.Data is Exception)
                                        Log.Error("Exception associated with task failure:", taskResult.Data as Exception);

									if (taskResult.Data is string)
										Log.InfoFormat("Additional Information: {0}", taskResult.Data);
									break;

								case Outcome.CriticalFailure:
									Queue.CriticalFail(taskResult.Reason);
									Log.InfoFormat("Task failed critically. Reason: {0}", taskResult.Reason);

									if (taskResult.Data is string)
										Log.InfoFormat("Additional Information: {0}", taskResult.Data);
									break;
                                case Outcome.Defer:
							        Queue.Defer(taskResult.Reason);
                                    Log.InfoFormat("Task deferred. Reason: {0}", taskResult.Reason);
                                    if (taskResult.Data is string)
                                        Log.InfoFormat("Additional Information: {0}", taskResult.Data);
							        break;
							}

							// if Stop() was invoked, break and let the parent loop handle it
							// (which will effectively break as well).
							if (!KeepWorking) break;
						}
					}
					catch (QueueIsEmptyException)
					{
						Log.DebugFormat("No tasks in queue [{0}]", new QueueName(Settings.Default.Queue).NameWhenPending);

						// The queue specified has run out of tasks. Unload the application domain to
						// conserve system resources.
						if (!releaseAppDomain())
						{
							Log.Fatal("Could not release application domain. Service is stopping.");
							throw;
						}
					}
					catch (Exception exception)
					{
						Log.Fatal("Fatal exception in monitor thread. Service is stopping.", exception);
						throw;
					}

					// A signal has been received to stop, or more tasks are readily available, so skip sleeping.
					if (NewTasksArrived)
					{
						Log.Debug("New tasks are available for processing. Will skip sleeping this time.");
						NewTasksArrived = false;
						continue;
					}

					if (!KeepWorking) continue;

					// Sleep for a while if no tasks have been made available in the mean time.
					Waiting = true;
					var waitStart = DateTime.Now;
					Log.DebugFormat("Waiting for: {0} millis.", Settings.Default.MonitorSleepIntervalInMilliseconds);
					Monitor.Wait(this, Settings.Default.MonitorSleepIntervalInMilliseconds);
					var intervalWaited = DateTime.Now - waitStart;
					Waiting = false;

					// Log the reason for resuming...
					Log.Debug(
						(intervalWaited.TotalMilliseconds + 100) < Settings.Default.MonitorSleepIntervalInMilliseconds
							? "Stopped waiting. Pulse received."
							: "Stopped waiting. Interval Elapsed.");
				}
			}
		}

		private bool releaseAppDomain()
		{
			if (Domain == null) return true;

			var retries = 0;
			while(retries++ <= Settings.Default.RetriesToUnloadAppDomain)
			{
				try
				{
					Log.Info("Attempting to release application domain.");

					if (Domain != null)
					{
						Log.DebugFormat("Domain Name: {0}", Domain.FriendlyName);
						AppDomain.Unload(Domain);
					}

					Performer = null;
					Domain = null;

					Log.Info("Succeeded.");

					return true;
				}
				catch (AppDomainUnloadedException appDomainUnloadedException)
				{
					Log.InfoFormat("Could not release domain. Retrying {0} out of {1}...",
						retries, Settings.Default.RetriesToUnloadAppDomain);

					Log.Debug("Exception Information", appDomainUnloadedException);
				}
			}
			
			return false;
		}

		private void initAppDomain()
		{
			var pathToAssembly = resolvePath(Settings.Default.WorkerAssemblyPath);

			if (String.IsNullOrEmpty(pathToAssembly))
				throw new FileNotFoundException("Could not locate the file to load.", 
					Settings.Default.WorkerAssemblyPath);

			var domainSetup = new AppDomainSetup { PrivateBinPath = pathToAssembly };

			Domain = AppDomain.CreateDomain("Domain_" + Guid.NewGuid(), null, domainSetup);

			Performer = (Performer)Domain.CreateInstanceFromAndUnwrap(
				pathToAssembly, Settings.Default.WorkerClassName);

			Log.Info("Initialized application domain.");
			Log.DebugFormat("AppDomain Name: {0}", Domain.FriendlyName);
        }

		private static string resolvePath(string workerAssemblyPath)
		{
			if (File.Exists(workerAssemblyPath)) return workerAssemblyPath;

			return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, workerAssemblyPath)) 
				? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, workerAssemblyPath) 
				: string.Empty;
		}
	}
}