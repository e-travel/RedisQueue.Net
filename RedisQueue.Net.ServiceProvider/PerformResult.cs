using System;

namespace RedisQueue.Net.ServiceProvider
{
	public class PerformResult : MarshalByRefObject, IPerformResult
	{
		public virtual Outcome Outcome { get; set; }
		public virtual string Reason { get; set; }
		public virtual object Data { get; set; }
	}
}