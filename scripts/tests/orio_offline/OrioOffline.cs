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
        // The rune dais: sockets turn their linked discs; all three discs must reach the target together.
        WorldObjectSnapshot[] discs = OrioRuneDiscPuzzle.DiscIds.Select((id,i)=>new WorldObjectSnapshot { id=id,mapId=OrioRuneDiscPuzzle.PuzzleMapId,
            rotation=OrioRuneDiscPuzzle.DiscStartRotations[i],tags=new[]{OrioRuneDiscPuzzle.DiscTag} }).ToArray();
        void Press(string socket)
        {
            Check(OrioRuneDiscPuzzle.TryGetLinkedDiscs(OrioRuneDiscPuzzle.PuzzleMapId,socket,out string[] linked),"socket links "+socket);
            foreach (string id in linked) { var disc=discs[Array.IndexOf(OrioRuneDiscPuzzle.DiscIds,id)]; disc.rotation=RotaraGearMechanism.NextQuarterTurn(disc.rotation); }
        }
        Check(!OrioRuneDiscPuzzle.AreDiscsAligned(OrioRuneDiscPuzzle.PuzzleMapId,discs),"authored start unsolved");
        string[] presses={"mechanism.orio.socket.north","mechanism.orio.socket.north","mechanism.orio.socket.north",OrioRuneDiscPuzzle.AnchorId,
            OrioRuneDiscPuzzle.AnchorId,"mechanism.orio.socket.west","mechanism.orio.socket.west","mechanism.orio.socket.east"};
        foreach (string socket in presses) { Check(!OrioRuneDiscPuzzle.AreDiscsAligned(OrioRuneDiscPuzzle.PuzzleMapId,discs),"not solved early"); Press(socket); }
        Check(OrioRuneDiscPuzzle.AreDiscsAligned(OrioRuneDiscPuzzle.PuzzleMapId,discs),"intended presses align all discs");
        Check(!OrioRuneDiscPuzzle.AreDiscsAligned("campus",discs),"campus map rejected");
        Check(!OrioRuneDiscPuzzle.TryGetLinkedDiscs("campus",OrioRuneDiscPuzzle.AnchorId,out _),"campus anchor is not a socket");
        Check(!OrioRuneDiscPuzzle.TryGetLinkedDiscs(OrioRuneDiscPuzzle.PuzzleMapId,OrioRuneDiscPuzzle.DiscIds[0],out _),"discs are not sockets");
        Check(!OrioRuneDiscPuzzle.TryGetLinkedDiscs("westForest",OrioRuneDiscPuzzle.AnchorId,out _),"forest pad is not a socket");
        discs[1].mapId="campus"; Check(!OrioRuneDiscPuzzle.AreDiscsAligned(OrioRuneDiscPuzzle.PuzzleMapId,discs),"saved snapshot map rejected");
        discs[1].mapId=OrioRuneDiscPuzzle.PuzzleMapId; discs[1].tags=Array.Empty<string>();
        Check(!OrioRuneDiscPuzzle.AreDiscsAligned(OrioRuneDiscPuzzle.PuzzleMapId,discs),"wrong tag rejected");
        discs[1].tags=new[]{OrioRuneDiscPuzzle.DiscTag};
        var db=JsonSerializer.Deserialize<GameEventDatabaseDefinition>(File.ReadAllText(args[0]),new JsonSerializerOptions{IncludeFields=true});
        var catalog=new GameEventCatalog(new[]{db}); Check(catalog.IsValid,"valid catalogue");
        var runtime=new GameEventRuntime(catalog);
        // Native WorldStateService cannot run outside Unity. Inject only its snapshot boundary;
        // execute the actual catalogue, custom-condition dispatch, dialogue and event runtime.
        string map=OrioRuneDiscPuzzle.PuzzleMapId;
        runtime.RegisterConditionHandler("OrioMechanismAligned",(condition,current)=>
            condition.key=="mechanism.orio.rotate" && current.GetQuestStatus("orio_patch_notes")==QuestStatus.Active &&
            current.GetFlag("spell.unlocked.Rotara") && OrioRuneDiscPuzzle.AreDiscsAligned(map,discs));
        Check(!runtime.TriggerEvent("complete_orio_patch_notes").Succeeded,"not started rejected");
        var session=runtime.StartDialogue("orio_patch_notes_dialogue"); Check(session.SelectChoice("accept"),"accept");
        Check(!session.IsCompleted,"acceptance alone unfinished");
        Check(!runtime.TriggerEvent("complete_orio_patch_notes").Succeeded,"unfinished lesson rejected");
        Check(session.Continue() && session.IsCompleted,"terminal lesson completed");
        Check(session.Definition.metadata.Any(m=>m.key=="spellRewardQuestId"&&m.value=="orio_patch_notes"),"Active lesson metadata");
        runtime.SetFlag("spell.unlocked.Rotara",true);
        discs[2].rotation=180; runtime.SetFlag("orio.patch_notes.mechanism_aligned",true);
        Check(!runtime.TriggerEvent("complete_orio_patch_notes").Succeeded,"stale flag does not bypass");
        session=runtime.StartDialogue("orio_patch_notes_dialogue");
        Check(!session.AvailableChoices.Any(c=>c.id=="report") && session.AvailableChoices.Any(c=>c.id=="active"),"wrong angle hint only"); session.End();
        discs[2].rotation=0; map="campus";
        Check(!runtime.TriggerEvent("complete_orio_patch_notes").Succeeded,"campus event rejected");
        map=OrioRuneDiscPuzzle.PuzzleMapId; session=runtime.StartDialogue("orio_patch_notes_dialogue");
        Check(session.AvailableChoices.Any(c=>c.id=="report"),"report unlocked");
        Check(session.SelectChoice("report"),"report selected");
        Check(runtime.GetQuestStatus("orio_patch_notes")==QuestStatus.Completed,"quest completes");
        Check(runtime.TriggerEvent("complete_orio_patch_notes").Status==GameEventTriggerStatus.AlreadyTriggered,"idempotent");
        Console.WriteLine($"PASS {checks} offline assertions against compiled production rotation, socket-link and dais target helpers and GameEventRuntime; native spell casts/save serialization remain Unity tests.");
    }
}
