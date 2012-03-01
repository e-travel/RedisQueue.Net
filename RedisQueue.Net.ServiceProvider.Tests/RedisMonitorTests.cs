using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Moq;
using NUnit.Framework;
using RedisQueue.Net.Clients;
using RedisQueue.Net.Clients.Entities;
using ThreadState = System.Threading.ThreadState;

namespace RedisQueue.Net.ServiceProvider.Tests
{
	[TestFixture]
	public class RedisMonitorTests
	{
		protected virtual Process RedisServer { get; set; }

		[TestFixtureSetUp]
		public void SetUp()
		{
			// kill any processes named "redis-server.exe" from the local machine.
			Process.GetProcessesByName("redis-server.exe").ToList().ForEach(p => p.Kill());

			var proc = new ProcessStartInfo
			{
				FileName = "fixtures\\Redis.64bit\\redis-server.exe",
				Arguments = "redis.conf",
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				WorkingDirectory = "fixtures\\Redis.64bit\\"
			};

			RedisServer = Process.Start(proc);
			RedisServer.OutputDataReceived += (sender, data) => Console.WriteLine(data.Data);
			RedisServer.ErrorDataReceived += (sender, data) => Console.WriteLine("ERROR: " + data.Data);
		}

		[TestFixtureTearDown]
		public void TearDown()
		{
			RedisServer.Kill();
			RedisServer.Dispose();
		}

		[Test]
		public void TestLoadsPerformerFixture()
		{
			var monitor = new RedisMonitor();
			Assert.IsNotNull(monitor.Performer);
		}

		[Test]
		public void TestCanStartAndStopNormally()
		{
			var monitor = new RedisMonitor();
			monitor.Start();
			Assert.IsTrue(monitor.Running);
			monitor.Stop();
			Assert.IsFalse(monitor.Running);
		}

		[Test]
		public void TestMonitorThreadStartsAndStopsNormally()
		{
			var monitor = new RedisMonitor();
			monitor.Start();
			Thread.Sleep(100);
			Assert.That(monitor.MonitorThread.ThreadState == ThreadState.Running);
			monitor.Stop();
			Thread.Sleep(1000);
			Assert.That(monitor.MonitorThread.ThreadState != ThreadState.Running);
		}

		[Test]
		public void TestProcessTaskWithNullTaskStorageSucceeds()
		{
			var monitor = new RedisMonitor();
			var task = new TaskMessage
			{
				Parameters = "Test Params",
				Queue = "TestQueue"
			};

			// Setup the Performer mock.
			var performerMock =new Mock<Performer>();
			performerMock.Setup(x => x.Perform(task.Parameters));
			performerMock.SetupGet(x => x.Status).Returns(new PerformResult
			{
				Data = string.Empty,
				Outcome = Outcome.Success,
				Reason = string.Empty
			});

			// Setup the Client mock.
			var clientMock = new Mock<QueueClient>();
			clientMock.Setup(x => x.Succeed());
			clientMock.SetupGet(x => x.RedisHost).Returns("127.0.0.1");
			clientMock.SetupGet(x => x.RedisPort).Returns(6379);
			
			monitor.Performer = performerMock.Object;
			monitor.MonitorClient = clientMock.Object;

			monitor.ProcessTask(task);

			Assert.That(performerMock.Object.TaskStorage == null);
			performerMock.Verify(x => x.Perform(task.Parameters));
			performerMock.VerifyGet(x => x.TaskStorage);
			performerMock.VerifyGet(x => x.Status);
			clientMock.Verify(x => x.Succeed());
		}
		
		[Test]
		public void TestProcessTaskWhenTaskPerformerThrowsException()
		{
			var monitor = new RedisMonitor();
			var task = new TaskMessage
			{
				Parameters = "Test Params",
				Queue = "TestQueue"
			};

			// Setup the Performer mock.
			var performerMock =new Mock<Performer>();
			var exception = new Exception("Test");
			performerMock.Setup(x => x.Perform(task.Parameters)).Throws(exception);
			performerMock.SetupGet(x => x.Status).Returns(new PerformResult
			{
				Data = string.Empty,
				Outcome = Outcome.Success,
				Reason = string.Empty
			});

			// Setup the Client mock.
			var clientMock = new Mock<QueueClient>();
			clientMock.SetupGet(x => x.RedisHost).Returns("127.0.0.1");
			clientMock.SetupGet(x => x.RedisPort).Returns(6379);
			clientMock.Setup(x => x.Fail(
				"The performer raised an exception: "
					+ exception.Message
					+ "\n"
					+ exception.StackTrace));
			
			monitor.Performer = performerMock.Object;
			monitor.MonitorClient = clientMock.Object;

			monitor.ProcessTask(task);

			performerMock.Verify(x => x.Perform(task.Parameters));
			performerMock.VerifyGet(x => x.TaskStorage);
			clientMock.Verify(x => x.Fail(
				"The performer raised an exception: "
					+ exception.Message
					+ "\n"
					+ exception.StackTrace));
		}

		[Test]
		public void TestProcessTaskFailure()
		{
			var monitor = new RedisMonitor();
			var task = new TaskMessage
			{
				Parameters = "Test Params",
				Queue = "TestQueue"
			};

			// Setup the Performer mock.
			var performerMock = new Mock<Performer>();
			performerMock.Setup(x => x.Perform(task.Parameters));
			performerMock.SetupGet(x => x.Status).Returns(new PerformResult
			{
				Data = string.Empty,
				Outcome = Outcome.Failure,
				Reason = string.Empty
			});

			// Setup the Client mock.
			var clientMock = new Mock<QueueClient>();
			clientMock.Setup(x => x.Fail(string.Empty));
			clientMock.SetupGet(x => x.RedisHost).Returns("127.0.0.1");
			clientMock.SetupGet(x => x.RedisPort).Returns(6379);

			monitor.Performer = performerMock.Object;
			monitor.MonitorClient = clientMock.Object;

			monitor.ProcessTask(task);

			performerMock.Verify(x => x.Perform(task.Parameters));
			performerMock.VerifyGet(x => x.TaskStorage);
			clientMock.Verify(x => x.Fail(string.Empty));
		}

		[Test]
		public void TestProcessTaskCriticalFailure()
		{
			var monitor = new RedisMonitor();
			var task = new TaskMessage
			{
				Parameters = "Test Params",
				Queue = "TestQueue"
			};

			// Setup the Performer mock.
			var performerMock = new Mock<Performer>();
			performerMock.Setup(x => x.Perform(task.Parameters));
			performerMock.SetupGet(x => x.Status).Returns(new PerformResult
			{
				Data = string.Empty,
				Outcome = Outcome.CriticalFailure,
				Reason = "blah"
			});

			// Setup the Client mock.
			var clientMock = new Mock<QueueClient>();
			clientMock.Setup(x => x.CriticalFail("blah"));
			clientMock.SetupGet(x => x.RedisHost).Returns("127.0.0.1");
			clientMock.SetupGet(x => x.RedisPort).Returns(6379);

			monitor.Performer = performerMock.Object;
			monitor.MonitorClient = clientMock.Object;

			monitor.ProcessTask(task);

			performerMock.Verify(x => x.Perform(task.Parameters));
			performerMock.VerifyGet(x => x.TaskStorage);
			clientMock.Verify(x => x.CriticalFail("blah"));
		}

		[Test]
		public void TestProcessPendingTasksWillFirstProcessCachedTask()
		{
			var monitor = new RedisMonitor();
			var task = new TaskMessage
			{
				Parameters = "Test Params",
				Queue = "TestQueue"
			};

			// Setup the Performer mock.
			var performerMock = new Mock<Performer>();
			performerMock.Setup(x => x.Perform(task.Parameters));
			performerMock.SetupGet(x => x.Status).Returns(new PerformResult
			{
				Data = string.Empty,
				Outcome = Outcome.Success,
				Reason = string.Empty
			});

			// Setup the Client mock.
			var clientMock = new Mock<QueueClient>();
			clientMock.Setup(x => x.Succeed());
			clientMock.SetupGet(x => x.CurrentTask).Returns(task);
			clientMock.SetupGet(x => x.RedisHost).Returns("127.0.0.1");
			clientMock.SetupGet(x => x.RedisPort).Returns(6379);

			monitor.Performer = performerMock.Object;
			monitor.MonitorClient = clientMock.Object;

			monitor.ProcessPendingTasks();

			performerMock.VerifyGet(x => x.TaskStorage);
			performerMock.VerifyGet(x => x.Status);
			clientMock.Verify(x => x.Succeed());
			clientMock.VerifyGet(x => x.CurrentTask);
		}

		[Test]
		public void TestProcessPendingTasksForMultipleTasksMockingPerformerOnly()
		{
			#region Prepare Tasks and mock Performer for processing
			var monitor = new RedisMonitor();
			var performerMock = new Mock<Performer>();

			using (var client = new QueueClient())
			{
				for (var i = 0; i < 10; i++)
				{
					var task = new TaskMessage
					{
						Parameters = "Task " + i,
						Queue = "TestQueue"
					};

					client.Enqueue(task);
					performerMock.Setup(x => x.Perform(task.Parameters));
				}
			}

			// ensure all tasks will be successful.
			performerMock.SetupGet(x => x.Status).Returns(new PerformResult
			{
				Data = string.Empty,
				Outcome = Outcome.Success,
				Reason = string.Empty
			});
			#endregion

			monitor.Performer = performerMock.Object;
			monitor.Start();
			Thread.Sleep(2000);
			monitor.Stop();


			for (var i = 0; i < 10; i++)
				performerMock.Verify(x => x.Perform("Task " + i));

			performerMock.VerifyGet(x => x.TaskStorage);
			performerMock.VerifyGet(x => x.Status);

			using(var client = new QueueClient())
				Assert.AreEqual(client.PendingTasks("TestQueue").Count, 0);
		}

		[Test]
		public void TestCanResolvePathToThisAssembly()
		{
			Assert.IsNotEmpty(RedisMonitor.ResolvePath("RedisQueue.Net.ServiceProvider.Tests.dll"));
		}

		[Test]
		public void TestCantResolveNonexistentAssemly()
		{
			Assert.IsEmpty(RedisMonitor.ResolvePath("Blah.dll"));
		}

		[Test]
		public void TestMonitorCanReceiveMessageFromChannel()
		{
			var task = new TaskMessage
			{
				Parameters = "blah",
				Queue = "TestQueue"
			};

			var performerMock = new Mock<Performer>();
			performerMock.SetupGet(x => x.Status).Returns(new PerformResult
			{
				Data = string.Empty,
				Outcome = Outcome.Success,
				Reason = string.Empty
			});

			performerMock.Setup(x => x.Perform(task.Parameters));

			var monitor = new RedisMonitor();
			monitor.Performer = performerMock.Object;
			monitor.Start();
			Assert.IsTrue(monitor.Running);

			using (var client = new QueueClient())
				client.Enqueue(task);

			monitor.Stop();
			Assert.IsFalse(monitor.Running);
		}
	}
}
