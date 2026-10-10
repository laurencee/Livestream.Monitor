using System.Collections.Generic;

namespace Livestream.Monitor.Model.ApiClients
{
    public interface IApiClientFactory
    {
        /// <summary> Gets a cached/singleton instance of stream provider <see cref="T"/> </summary>
        T Get<T>() where T : IApiClient;

        IEnumerable<IApiClient> GetAll();

        IApiClient GetByName(string name);
    }
}