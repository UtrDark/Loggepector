using System.Collections.Generic;
using Loggepector.Models;

namespace Loggepector.Services;

public interface IPlayerJoinService
{
    IReadOnlyDictionary<string, IReadOnlyList<PlayerJoin>> PlayerJoins { get; }
    IReadOnlyDictionary<string, IReadOnlyList<PlayerAntagonist>> PlayerAntagonists { get; }
    void Process(IEnumerable<ILogEntry> miscEntries);
}
