using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RedisQueue.Net.Clients.Exceptions;

namespace RedisQueue.Net.Clients.Entities
{
	/// <summary>
	/// POCO keeping a message regarding a specific task.
	/// </summary>
	public class TaskMessage
	{
		private static readonly Regex ValidNamePattern = new Regex(@"^[a-zA-Z]{1}[\w_]+$", RegexOptions.Compiled);

		/// <summary>
		/// A unique identifier for the task.
		/// </summary>
		public virtual Guid Identifier { get; set; }

		private string _queue;

		/// <summary>
		/// The task's initial queue. This value does not get updated when the task moves from
		/// this designated queue to either the :failed or :succeeded queues, subsequently.
		/// </summary>
		public virtual string Queue
		{
			get { return _queue; }
			set
			{
				if (value.ToLowerInvariant().Contains("succeeded")
					|| value.ToLowerInvariant().Contains("failed")
					|| !ValidNamePattern.IsMatch(value))

					throw new InvalidQueueNameException(
						"Queue name cannot contain colons (:), or the literals 'failed', 'succeeded', and must be at least 2 characters long.");

				_queue = value;
			}
		}

		/// <summary>
		/// Any data that should be part of the task message.
		/// </summary>
		public virtual string Parameters { get; set; }

		/// <summary>
		/// Storage specific to the task, to maintain its state cross-context.
		/// </summary>
		public virtual IDictionary<string, string> Storage { get; set; }

		/// <summary>
		/// The status of the task.
		/// </summary>
		public virtual TaskStatus Status { get; set; }

		/// <summary>
		/// The reason for the task's status; usually null or empty, except in the 
		/// case of task failure, where the task's consumer is expected to provide a reason.
		/// </summary>
		public virtual string Reason { get; set; }

		/// <summary>
		/// The task's creation date.
		/// </summary>
		public virtual DateTime CreatedOn { get; set; }

		/// <summary>
		/// The task's latest update date, if available.
		/// </summary>
		public virtual DateTime? UpdatedOn { get; set; }

		/// <summary>
		/// The number of retries before placing the task in the failed queue.
		/// </summary>
		public virtual int Retries { get; set; }

		/// <summary>
		/// Ctor. Creates a new identifier, sets the status to <c>NotStarted</c>, and sets the
		/// CreatedOn date to <c>DateTime.Now</c>.
		/// </summary>
		public TaskMessage()
		{
			Identifier = Guid.NewGuid();
			Status = TaskStatus.NotStarted;
			CreatedOn = DateTime.Now;
			Storage = new Dictionary<string, string>();
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != typeof (TaskMessage)) return false;
			return Equals((TaskMessage) obj);
		}

		public bool Equals(TaskMessage other)
		{
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return Equals(other._queue, _queue) && other.Identifier.Equals(Identifier) && Equals(other.Parameters, Parameters);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int result = (_queue != null ? _queue.GetHashCode() : 0);
				result = (result*397) ^ Identifier.GetHashCode();
				result = (result*397) ^ (Parameters != null ? Parameters.GetHashCode() : 0);
				return result;
			}
		}
	}
}