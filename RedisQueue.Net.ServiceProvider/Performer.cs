using System.Collections.Generic;
using RedisQueue.Net.ServiceProvider.Old;

namespace RedisQueue.Net.ServiceProvider
{
	public abstract class Performer
	{
		public abstract void Perform(string args);
		public virtual IPerformResult Status { get; protected set; }
		public virtual IDictionary<string, string> TaskStorage { get; set; }

		protected Performer()
		{
			Status = new PerformResult { Outcome = Outcome.NotStarted };
			TaskStorage = new Dictionary<string, string>();
		}
	}
}