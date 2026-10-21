using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public interface IMapViewService
{
    Task<MapDocument> LoadAsync(string mapName, CancellationToken cancellationToken = default);
}
