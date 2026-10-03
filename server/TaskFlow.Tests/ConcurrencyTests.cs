using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Tests;

public class ConcurrencyTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Second_editor_with_an_old_version_gets_a_conflict_instead_of_overwriting()
    {
        var sara = _t.AddUser("Sara");
        var (_, columns) = await _t.CreateBoardAsync(sara);
        var original = await _t.AddTaskAsync(sara, columns[0].Id, "Login page");

        // Sara and Ali both opened the task and saw the same RowVersion.
        var seenBySara = original.RowVersion;
        var seenByAli = original.RowVersion;

        // Sara saves first (separate DbContext = separate web request).
        await using (var db = _t.NewContext())
            await _t.Tasks(db).UpdateAsync(original.Id, sara, Edit("Login page (Sara)", seenBySara));

        // Ali saves later with his old version: he must not silently overwrite Sara's change.
        await using (var db = _t.NewContext())
        {
            var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
                _t.Tasks(db).UpdateAsync(original.Id, sara, Edit("Login page (Ali)", seenByAli)));

            var latest = Assert.IsType<TaskDto>(conflict.Current);
            Assert.Equal("Login page (Sara)", latest.Title);
        }
    }

    [Fact]
    public async Task Editing_with_the_current_version_succeeds_and_returns_a_new_version()
    {
        var sara = _t.AddUser("Sara");
        var (_, columns) = await _t.CreateBoardAsync(sara);
        var task = await _t.AddTaskAsync(sara, columns[0].Id, "Login page");

        await using var db = _t.NewContext();
        var updated = await _t.Tasks(db).UpdateAsync(task.Id, sara, Edit("Login page v2", task.RowVersion));

        Assert.Equal("Login page v2", updated.Title);
        Assert.NotEqual(task.RowVersion, updated.RowVersion);
    }

    private static UpdateTaskRequest Edit(string title, byte[] rowVersion) =>
        new(title, null, null, null, TaskPriority.High, rowVersion);
}
