using Game.Application;
using Game.Core.Model;
using Game.Core.Story;

namespace Game.Tests;

public sealed class StoryParallelCommandTests
{
    [Fact]
    public async Task Parallel_RunsEveryTrackBeforeContinuing()
    {
        var script = StoryScriptJson.Parse(
            """
            {
              "version": 3,
              "segments": [
                {"name": "main", "steps": [
                  {"kind": "command", "call": "parallel(['track_a', 'track_b'])"},
                  {"kind": "command", "call": "set_flag('after')"}
                ]},
                {"name": "track_a", "steps": [{"kind": "command", "call": "set_flag('a')"}]},
                {"name": "track_b", "steps": [{"kind": "command", "call": "set_flag('b')"}]}
              ]
            }
            """,
            "parallel-test");
        var session = new GameSession(new GameState(), TestContentFactory.CreateRepository(storyScripts: [script]));

        await session.StoryService.ExecuteAsync("main");

        Assert.True(session.State.Story.Variables.ContainsKey("a"));
        Assert.True(session.State.Story.Variables.ContainsKey("b"));
        Assert.True(session.State.Story.Variables.ContainsKey("after"));
    }

    [Fact]
    public void ContentValidation_RejectsDialogueInsideParallelTrack()
    {
        var package = new Game.Content.Loading.ContentPackage();
        package.StoryScripts["test"] = StoryScriptJson.Parse(
            """
            {
              "version": 3,
              "segments": [
                {"name": "main", "steps": [{"kind": "command", "call": "parallel(['talk'])"}]},
                {"name": "talk", "steps": [{"kind": "dialogue", "speaker": "a", "text": "hi"}]}
              ]
            }
            """);

        var exception = Assert.ThrowsAny<Exception>(() => new Game.Content.Loading.JsonContentLoader().LoadFromPackage(package));

        Assert.Contains("parallel track 'talk' may only contain commands", exception.Message);
    }
}
