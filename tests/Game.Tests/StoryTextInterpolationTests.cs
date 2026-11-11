using Game.Application;
using Game.Core.Model;
using Game.Core.Story;

namespace Game.Tests;

public sealed class StoryTextInterpolationTests
{
    [Fact]
    public async Task Placeholders_ResolveThroughStoryVariablesAndLegacyAliases()
    {
        var script = StoryScriptJson.Parse("""
        {"version":3,"segments":[{"name":"talk","steps":[
          {"kind":"set","target":"quest","value":"3"},
          {"kind":"dialogue","speaker":"$MALE$","text":"$FEMALE$|$heroine_name$|$ZHENLONG_LEVEL$|$silver$|$quest$|$target$|$missing$"}
        ]}]}
        """);
        var hero = TestContentFactory.CreateCharacterDefinition("主角") with { Name = "小虾米" };
        var heroine = TestContentFactory.CreateCharacterDefinition("女主") with { Name = "铃兰" };
        var host = new RecordingDialogueHost();
        var state = new GameState();
        state.Party.AddMember(TestContentFactory.CreateCharacterInstance("主角", hero));
        state.Currency.AddSilver(120);
        var session = new GameSession(
            state,
            TestContentFactory.CreateRepository(characters: [hero, heroine], storyScripts: [script]),
            host);
        session.State.Party.GetMember("主角").Name = "张三";
        var context = new StoryExecutionContext(new Dictionary<string, ExpressionValue>
        {
            ["target"] = ExpressionValue.FromString("郭靖"),
        });

        await session.StoryService.ExecuteAsync("talk", context);

        var dialogue = Assert.Single(host.Dialogues);
        Assert.Equal("张三", dialogue.Speaker);
        Assert.Equal("铃兰|铃兰|1|120|3|郭靖|$missing$", dialogue.Text);
    }

    [Fact]
    public void HeroNames_AreBuiltInExpressionVariables()
    {
        var hero = TestContentFactory.CreateCharacterDefinition("主角") with { Name = "小虾米" };
        var session = new GameSession(new GameState(), TestContentFactory.CreateRepository(characters: [hero]));
        var environment = new GameExpressionEnvironment(session).Create();

        Assert.True(new ExpressionEvaluator().Evaluate(
            new ExpressionParser().ParseExpression("hero_name == '小虾米'"), environment).AsBoolean("test"));
        Assert.Contains("hero_name", GameExpressionSymbols.BuiltInVariables);
    }

    private sealed class RecordingDialogueHost : IRuntimeHost
    {
        public List<DialogueContext> Dialogues { get; } = [];

        public ValueTask PlayEffectAsync(string effectId, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DialogueAsync(DialogueContext dialogue, CancellationToken cancellationToken)
        {
            Dialogues.Add(dialogue);
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ChooseOptionAsync(ChoiceContext choice, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<BattleOutcome> ResolveBattleAsync(BattleContext battle, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
