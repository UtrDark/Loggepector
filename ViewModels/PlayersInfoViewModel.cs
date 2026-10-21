using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Loggepector.Services;

namespace Loggepector.ViewModels;

public sealed partial class PlayersInfoViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    public IReadOnlyList<PlayerInfoViewModel> Antagonists { get; }
    public IReadOnlyList<PlayerDepartmentViewModel> Departments { get; }
    public bool HasAntagonists => Antagonists.Count > 0;
    public bool HasManifest => Departments.Count > 0;
    public bool IsEmpty => !HasAntagonists && !HasManifest;

    public PlayersInfoViewModel(ILogService logService, Action<string> selectPlayer,
        Func<string, PlayerViewModel> getPlayer)
    {
        var antagonistColor = Brush.Parse("#BB3650");
        Antagonists = logService.GetPlayerAntagonists().OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Distinct().Select(role => new PlayerInfoViewModel(role.Role,
                $"{role.CharName} ({pair.Key})", role.SelectionType, antagonistColor, pair.Key, selectPlayer,
                getPlayer(pair.Key)))).ToArray();

        var joins = logService.GetPlayerJoins().OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Distinct().Select(join => (Key: pair.Key, Join: join))).ToArray();
        var departments = new List<PlayerDepartmentViewModel>();
        foreach (var department in PlayersInfoConfig.Departments)
        {
            var accent = Brush.Parse(department.Color);
            var players = joins.Where(player => department.Jobs.Contains(player.Join.Job, StringComparer.OrdinalIgnoreCase))
                .Select(player => CreateCard(player.Key, player.Join.CharName, player.Join.Job, player.Join.JoinType, accent,
                    selectPlayer, getPlayer(player.Key)))
                .ToArray();
            if (players.Length > 0)
                departments.Add(new PlayerDepartmentViewModel(department.Name, accent, players));
        }

        var knownJobs = PlayersInfoConfig.Departments.SelectMany(department => department.Jobs)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var otherColor = Brush.Parse("#596577");
        var others = joins.Where(player => !knownJobs.Contains(player.Join.Job))
            .Select(player => CreateCard(player.Key, player.Join.CharName, player.Join.Job, player.Join.JoinType, otherColor,
                selectPlayer, getPlayer(player.Key)))
            .ToArray();
        if (others.Length > 0)
            departments.Add(new PlayerDepartmentViewModel("OTHER", otherColor, others));
        Departments = departments;
    }

    private static PlayerInfoViewModel CreateCard(string key, string name, string job, string joinType, IBrush accent,
        Action<string> selectPlayer, PlayerViewModel selection) =>
        new(name, job, string.Equals(joinType, "ROUNDSTART", StringComparison.OrdinalIgnoreCase)
            ? key : $"{key} · {joinType.ToLowerInvariant()}", accent, key, selectPlayer, selection);

    partial void OnSearchTextChanged(string value)
    {
        var query = value.Trim();
        foreach (var card in Antagonists.Concat(Departments.SelectMany(department => department.Players)))
        {
            card.IsSearchMatch = query.Length > 0 &&
                (card.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 card.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 card.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
    }
}
