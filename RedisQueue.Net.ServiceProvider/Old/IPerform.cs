using System.Collections.Generic;

namespace RedisQueue.Net.ServiceProvider.Old
{
	public interface IPerform
	{
		void Perform(string args);
		IPerformResult Status { get; }
		IDictionary<string, string> TaskStorage { get; set; }
	}
}