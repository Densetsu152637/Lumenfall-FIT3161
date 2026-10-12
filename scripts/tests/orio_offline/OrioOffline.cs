using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Lumenfall.GameEvents;
using Lumenfall.Quests;
using Lumenfall.Spells;
public static class OrioOffline
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    public static void Main(string[] args)
    {
        foreach (var item in new[] { (180f,270f), (270f,0f), (0f,90f), (-90f,0f), (360f,90f), (450f,180f) })
            Check(RotaraGearMechanism.NextQuarterTurn(item.Item1) == item.Item2, "quarter turn " + item.Item1);
        foreach (float angle in new[] { 90f,450f,-270f }) Check(RotaraGearMechanism.RotationMatches(angle,90), "normalized target");
        foreach (float angle in new[] { 0f,180f,270f,89.5f,float.NaN,float.PositiveInfinity,float.NegativeInfinity })
            Check(!RotaraGearMechanism.RotationMatches(angle,90), "reject wrong/nonfinite angle");
        WorldObjectSnapshot snapshot = new WorldObjectSnapshot { id="mechanism.orio.rotate",mapId="westForest",rotation=90,tags=new[]{"rotara"} };
        Check(RotaraGearMechanism.MatchesQuestTarget("westForest",snapshot), "exact target");
        Check(!RotaraGearMechanism.MatchesQuestTarget("campus",snapshot), "campus map rejected");
        snapshot.id="other"; Check(!RotaraGearMechanism.MatchesQuestTarget("westForest",snapshot),"wrong id rejected");
        snapshot.id="mechanism.orio.rotate"; snapshot.mapId="campus";
        Check(!RotaraGearMechanism.MatchesQuestTarget("westForest",snapshot),"saved snapshot map rejected");
        snapshot.mapId="westForest"; snapshot.tags=Array.Empty<string>();
        Check(!RotaraGearMechanism.MatchesQuestTarget("westForest",snapshot),"wrong tag rejected");
        snapshot.tags=new[]{"rotara"};
        var db=JsonSerializer.Deserialize<GameEventDatabaseDefinition>(File.ReadAllText(args[0]),new JsonSerializerOptions{IncludeFields=true});
        var catalog=new GameEventCatalog(new[]{db}); Check(catalog.IsValid,"valid catalogue");
        var runtime=new GameEventRuntime(catalog);
        // Native WorldStateService cannot run outside Unity. Inject only its snapshot boundary;
        // execute the actual catalogue, custom-condition dispatch, dialogue and event runtime.
        string map="westForest";
        runtime.RegisterConditionHandler("OrioMechanismAligned",(condition,current)=>
            condition.key=="mechanism.orio.rotate" && current.GetQuestStatus("orio_patch_notes")==QuestStatus.Active &&
            current.GetFlag("spell.unlocked.Rotara") && RotaraGearMechanism.MatchesQuestTarget(map,snapshot));
        Check(!runtime.TriggerEvent("complete_orio_patch_notes").Succeeded,"not started rejected");
        var session=runtime.StartDialogue("orio_patch_notes_dialogue"); Check(session.SelectChoice("accept"),"accept");
        Check(!session.IsCompleted,"acceptance alone unfinished");
        Check(!runtime.TriggerEvent("complete_orio_patch_notes").Succeeded,"unfinished lesson rejected");
        Check(session.Continue() && session.IsCompleted,"terminal lesson completed");
        Check(session.Definition.metadata.Any(m=>m.key=="spellRewardQuestId"&&m.value=="orio_patch_notes"),"Active lesson metadata");
        runtime.SetFlag("spell.unlocked.Rotara",true);
        snapshot.rotation=180; runtime.SetFlag("orio.patch_notes.mechanism_aligned",true);
        Check(!runtime.TriggerEvent("complete_orio_patch_notes").Succeeded,"stale flag does not bypass");
        session=runtime.StartDialogue("orio_patch_notes_dialogue");
        Check(!session.AvailableChoices.Any(c=>c.id=="report") && session.AvailableChoices.Any(c=>c.id=="active"),"wrong angle hint only"); session.End();
        snapshot.rotation=90; map="campus";
        Check(!runtime.TriggerEvent("complete_orio_patch_notes").Succeeded,"campus event rejected");
        map="westForest"; session=runtime.StartDialogue("orio_patch_notes_dialogue");
        Check(session.AvailableChoices.Any(c=>c.id=="report"),"report unlocked");
        Check(session.SelectChoice("report"),"report selected");
        Check(runtime.GetQuestStatus("orio_patch_notes")==QuestStatus.Completed,"quest completes");
        Check(runtime.TriggerEvent("complete_orio_patch_notes").Status==GameEventTriggerStatus.AlreadyTriggered,"idempotent");
        Console.WriteLine($"PASS {checks} offline assertions against compiled production rotation/target helpers and GameEventRuntime; native spell casts/save serialization remain Unity tests.");
    }
}
