using System;
using System.Collections.Generic;

namespace RedisQueue.Net.ServiceProvider.Old
{
	public abstract class Performer : MarshalByRefObject
	{
		public abstract void Perform(string args);
		public IPerformResult Status { get; protected set; }
		public IDictionary<string, string> TaskStorage { get; set; }

		protected Performer()
		{
			Status = new PerformResult { Outcome = Outcome.NotStarted };
			TaskStorage = new Dictionary<string, string>();
		}

		// The (pointless?) override below is to insure that 
		// when created as a Singleton, the instance never dies, no
		// matter how long between calls.
		public override object InitializeLifetimeService()
		{
			return null;
		}
	}
}