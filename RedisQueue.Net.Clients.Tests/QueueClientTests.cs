using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using RedisQueue.Net.Clients.Entities;
using RedisQueue.Net.Clients.Exceptions; 

namespace RedisQueue.Net.Clients.Tests
{
	[TestFixture]
	public class QueueClientTests
	{
		protected virtual Process RedisServer { get; set; }

		[TestFixtureSetUp]
		public void SetUp()
		{
			var proc = new ProcessStartInfo
			{
				FileName = "fixtures\\Redis.64bit\\redis-server.exe",
				Arguments = "redis.conf",
				CreateNoWindow = true,
				RedirectStandardError = false,
				RedirectStandardOutput = false,
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

			try { File.Delete("redisCache.bin"); } catch {}
		}

		[Test]
		public void TestCanEnqueueTaskInRedisAndReadItBack()
		{
			using(var client = new QueueClient())
			{
				var homemadeTask = new TaskMessage
				{
					Parameters = "params", 
					Queue = "TestQueue"
				};

				client.Enqueue(homemadeTask);
				var retrievedTask = client.Reserve("TestQueue");
			
				Assert.AreEqual(retrievedTask.Parameters, homemadeTask.Parameters);
				Assert.AreEqual(retrievedTask.Queue, homemadeTask.Queue);

				client.Fail(string.Empty);
				client.RemoveTask(client.AllTasks("TestQueue")[0]);
			}
		}

		[Test]
		public void TestCachingCreatesAFile()
		{
			using (var client = new QueueClient())
			{
				var homemadeTask = new TaskMessage
				{
					Parameters = "params",
					Queue = "TestQueue"
				};

				client.Enqueue(homemadeTask);
				var retrievedTask = client.Reserve("TestQueue");

				Assert.That(File.Exists("redisCache.bin"));

				client.Fail(string.Empty);
				client.RemoveTask(client.AllTasks("TestQueue")[0]);
			}
		}

		[Test]
		public void TestCachingCanPreserveTask()
		{
			using (var client = new QueueClient())
			{
				var homemadeTask = new TaskMessage
				{
					Parameters = "params",
					Queue = "TestQueue"
				};

				client.Enqueue(homemadeTask);
				var retrievedTask = client.Reserve("TestQueue");
			}

			using(var client = new QueueClient())
			{
				Assert.That(client.CurrentTask != null);
				client.Fail(string.Empty);
				client.RemoveTask(client.AllTasks("TestQueue")[0]);
			}
		}

		[Test]
		public void TestEnqueueAndDelete100Tasks()
		{
			var client = new QueueClient();
			for(var i = 0; i < 100; i++)
			{
				client.Enqueue(new TaskMessage
				{
					Parameters = "params",
					Queue = "TestQueue"
				});
			}

			Assert.AreEqual(client.AllTasks("TestQueue").Count, 100);

			var deletedTasks = 0;
			while(client.AllTasks("TestQueue").Count > 0)
			{
				client.RemoveTask(client.AllTasks("TestQueue")[0]);
				deletedTasks++;
			}

			Assert.AreEqual(deletedTasks, 100);
		}

		[Test]
		public void TestThrowsExceptionOnRedisDeath()
		{
			var client = new QueueClient();
			RedisServer.Kill();

			Assert.Throws<IOException>(() => client.Enqueue(new TaskMessage
			{
				Parameters = "params",
				Queue = "TestQueue"
			}));

			RedisServer.Start();
		}

		[Test]
		public void TestEmptyQueueThrowsException()
		{
			using (var client = new QueueClient())
				Assert.Throws<QueueIsEmptyException>(() => client.Reserve("messages"));
		}

		[Test]
		public void TestCantEnqueueWithoutSettingAQueueInTheTaskMessage()
		{
			using(var client = new QueueClient())
				Assert.Throws<NoQueueSpecifiedException>(
					() => client.Enqueue(new TaskMessage{Parameters = "Test"}));
		}

		[Test]
		public void TestSendMessageThroughRedis()
		{
			var receiver = new QueueClient();
			var subscription = receiver.GetSubscription();
			bool messageReceived;
			messageReceived = false; 

			subscription.OnMessage = (queue, message) =>
			{
				messageReceived = true;
				Assert.AreEqual(queue, "TestQueue");
				Assert.AreEqual(message, "Test Message");
			};

			Action a = () => subscription.SubscribeToChannels(new QueueName("TestQueue").ChannelName);
			a.BeginInvoke(null, null);

			var sender = new QueueClient();
			sender.SendMessage("Test Message", "TestQueue");

			Thread.Sleep(100);
			Assert.IsTrue(messageReceived);
		}

		[Test]
		public void TestCantReserveMultipleTasksAtOnce()
		{
			using(var client = new QueueClient())
			{
				client.Enqueue(new TaskMessage
				{
					Parameters = "params",
					Queue = "TestQueue"
				});

				client.Enqueue(new TaskMessage
				{
					Parameters = "params",
					Queue = "TestQueue"
				});

				client.Reserve("TestQueue");
				Assert.Throws<TaskAlreadyReservedException>(() => client.Reserve("TestQueue"));

				client.Fail(string.Empty);
				client.RemoveTask(client.AllTasks("TestQueue")[0]);
				client.RemoveTask(client.AllTasks("TestQueue")[0]);
			}
		}

		[Test]
		public void TestReserveThrowsExceptionWhenPassedAnEmptyStringAsQueue()
		{
			using(var client = new QueueClient())
				Assert.Throws<InvalidQueueNameException>(() => client.Reserve(string.Empty));
		}

		[Test]
		public void TestReserveThrowsExceptionWhenQueueIsEmpty()
		{
			using (var client = new QueueClient())
				Assert.Throws<QueueIsEmptyException>(() => client.Reserve("TestQueue"));
		}

		[Test]
		public void TestModifyTaskOnCollectionThenAssertModificationDoesNotWork()
		{
			using(var client = new QueueClient())
			{
				// first enqueue a simple task.
				client.Enqueue(new TaskMessage
				{
					Parameters = "Test Params",
					Queue = "TestQueue"
				});

				// now modify the task from within the collection.
				client.PendingTasks("TestQueue")[0].Parameters = "Test Params 2";
			}

			// Dispose of the previous client to ensure modifications dont persist in its memory.
			using(var client = new QueueClient())
			{
				// using a new client, read the task again.
				var task = client.PendingTasks("TestQueue")[0];

				Assert.AreEqual(task.Parameters, "Test Params");

				client.RemoveTask(client.AllTasks("TestQueue")[0]);
			}
		}
	}
}