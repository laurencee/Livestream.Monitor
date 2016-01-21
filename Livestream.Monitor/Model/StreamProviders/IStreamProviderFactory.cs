using System.Collections.Generic;

namespace Livestream.Monitor.Model.StreamProviders
{
    public interface IStreamProviderFactory
    {
        /// <summary> Gets a cached/singleton instance of stream provider <see cref="T"/> </summary>
        T Get<T>() where T : IStreamProvider;

        IEnumerable<IStreamProvider> GetAll();

        IStreamProvider GetStreamProviderByName(string streamProviderName);
    }
}