using System.Text.RegularExpressions;
using RedisQueue.Net.Clients.Exceptions;

namespace RedisQueue.Net.Clients.Entities
{
	public class QueueName
	{
		private static readonly Regex ValidNamePattern = new Regex(@"^[a-zA-Z]{1}[\w_]+$", RegexOptions.Compiled);

		private readonly string _name;

		public QueueName(string name)
		{
			if (name.ToLowerInvariant().Contains("succeeded")
					|| name.ToLowerInvariant().Contains("failed")
					|| !ValidNamePattern.IsMatch(name))

				throw new InvalidQueueNameException(
					"Queue name cannot contain colons (:), or the literals 'failed', 'succeeded', and must be at least 2 characters long.");

			_name = name;
		}

		private string _nameWhenPending;
		public virtual string NameWhenPending
		{
			get { return _nameWhenPending ?? (_nameWhenPending = "queue:" + _name + ":pending"); }
		}

		private string _nameWhenFailed;
		public virtual string NameWhenFailed
		{
			get { return _nameWhenFailed ?? (_nameWhenFailed = "queue:" + _name + ":failed"); }
		}

		private string _nameWhenSucceeded;
		public virtual string NameWhenSucceeded
		{
			get
			{
				return _nameWhenSucceeded ?? (_nameWhenSucceeded = "queue:" + _name + ":succeeded");
			}
		}

		private string _channelName;
		public virtual string ChannelName
		{
			get { return _channelName ?? (_channelName = "queue:" + _name + ":channel"); }
		}

		public override string ToString()
		{
			return _name;
		}

		public static implicit operator QueueName(string name)
		{
			return new QueueName(name);
		}
	}
}
