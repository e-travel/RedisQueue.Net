using System;
using System.Collections.Generic;
using RedisQueue.Net.Clients.Entities;
using RedisQueue.Net.Clients.Exceptions;
using ServiceStack.Redis;

namespace RedisQueue.Net.Clients
{
	public interface IQueueClient : IDisposable
	{
		/// <summary>
		/// The task currently reserved, if any.
		/// </summary>
		TaskMessage CurrentTask { get; }

		/// <summary>
		/// Adds a task to the queue specified in the task itself and sends a message to
		/// the associated channel, notifying any listeners.
		/// </summary>
		/// <param name="t"></param>
		void Enqueue(TaskMessage t);

		/// <summary>
		/// Obtains the first available task from the specified queue, and changes the client's
		/// state into TaskReserved. In that state, no new tasks can be reserved until the 
		/// task's outcome is evident.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		TaskMessage Reserve(QueueName queue);

		/// <summary>
		/// Obtains the first available task from the specified queue, and changes the client's
		/// state into TaskReserved. In that state, no new tasks can be reserved until the 
		/// task's outcome is evident.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		TaskMessage Reserve(string queue);

		/// <summary>
		/// Marks the currently reserved task as failed.
		/// </summary>
		/// <param name="reason"></param>
		/// <exception cref="NoTaskReservedException">No task has been reserved. Future failure not yet 
		/// supported.</exception>
		/// <exception cref="InvalidStateException">CurrentTask is null. Something's seriously off.</exception>
		void Fail(string reason);

		/// <summary>
		/// Marks the current task as failed, and regardless of the cycling setting, places it 
		/// in the :failed queue.
		/// </summary>
		/// <param name="reason"></param>
		void CriticalFail(string reason);

		/// <summary>
		/// Marks the currently reserved task as succeeded.
		/// </summary>
		/// <exception cref="InvalidStateException">CurrentTask is null. Something's seriously off.</exception>
		/// <exception cref="NoTaskReservedException">No task has been reserved. Future success not yet 
		/// supported.</exception>
		void Succeed();

		/// <summary>
		/// Returns all pending tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		IList<TaskMessage> PendingTasks(QueueName queue);

		/// <summary>
		/// Returns all pending tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		IList<TaskMessage> PendingTasks(string queue);

		/// <summary>
		/// Returns all failed tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		IList<TaskMessage> FailedTasks(QueueName queue);

		/// <summary>
		/// Returns all failed tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		IList<TaskMessage> FailedTasks(string queue);

		/// <summary>
		/// Returns all tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		IList<TaskMessage> AllTasks(QueueName queue);

		/// <summary>
		/// Returns all tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		IList<TaskMessage> AllTasks(string queue);

		/// <summary>
		/// Returns all succeeded tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		IList<TaskMessage> SucceededTasks(QueueName queue);

		/// <summary>
		/// Returns all succeeded tasks.
		/// </summary>
		/// <param name="queue"></param>
		/// <returns></returns>
		IList<TaskMessage> SucceededTasks(string queue);

		/// <summary>
		/// Returns a list of all queues found.
		/// </summary>
		/// <returns></returns>
		IList<string> AllQueues();

		/// <summary>
		/// Removes a task with a specific task identifier.
		/// </summary>
		/// <param name="task"></param>
		void RemoveTask(TaskMessage task);

		/// <summary>
		/// Returns an empty channel subscription.
		/// </summary>
		/// <returns></returns>
		IRedisSubscription GetSubscription();

		/// <summary>
		/// Sends a message to the channel associated with a specific queue.
		/// </summary>
		/// <param name="message"></param>
		/// <param name="queue"></param>
		void SendMessage(string message, QueueName queue);

		/// <summary>
		/// Sends a message to the channel associated with a specific queue.
		/// </summary>
		/// <param name="message"></param>
		/// <param name="queue"></param>
		void SendMessage(string message, string queue);
	}
}