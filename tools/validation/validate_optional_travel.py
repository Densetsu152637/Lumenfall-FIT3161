"""Execute optional travel/ambient rules from current C# source without Unity.
The tiny Unity value/JSON adapter does not validate scene/lifecycle/rendering behavior.
"""
import argparse
import json
from pathlib import Path
import subprocess

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--unity-data", type=Path, default=Path("C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor/Data"))
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    systems = root / "Lumenfall/Assets/_Project/Systems"
    output = root / "Lumenfall/Temp/optional-travel-policy-harness"
    output.mkdir(parents=True, exist_ok=True)
    shared = args.unity_data / "NetCoreRuntime/shared/Microsoft.NETCore.App"
    version = sorted(shared.iterdir(), key=lambda p: tuple(map(int, p.name.split("."))))[-1]
    shim = output / "UnityValues.cs"
    shim.write_text(SHIM, encoding="utf-8")
    harness = output / "PolicyHarness.cs"
    harness.write_text(HARNESS, encoding="utf-8")
    sources = [
        systems / "GameEvents/GameEventDefinitions.cs",
        systems / "GameEvents/GameEventCatalog.cs",
        systems / "GameEvents/GameEventRuntime.cs",
        systems / "GameEvents/DialogueSession.cs",
        systems / "DataDriven/MapAndNpcDefinitions.cs",
        systems / "DataDriven/WorldAreaAsset.cs",
        systems / "DataDriven/PortalContentAsset.cs",
        systems / "DataDriven/AmbientNpcPolicy.cs",
        systems / "DataDriven/OptionalTravelPolicy.cs",
        shim, harness,
    ]
    assembly = output / "PolicyHarness.dll"
    response = output / "PolicyHarness.rsp"
    response.write_text("\n".join([
        "-target:exe", "-nologo", "-langversion:9.0", '-out:"%s"' % assembly,
        *['-r:"%s"' % p for p in version.glob("*.dll") if (p.name.startswith("System.") or p.name in ("mscorlib.dll", "netstandard.dll", "Microsoft.CSharp.dll")) and ".Native." not in p.name],
        *['"%s"' % p for p in sources],
    ]), encoding="utf-8")
    runner = args.unity_data / "NetCoreRuntime/dotnet.exe"
    subprocess.run([str(runner), str(args.unity_data / "DotNetSdkRoslyn/csc.dll"), "@" + str(response)], check=True)
    (output / "PolicyHarness.runtimeconfig.json").write_text(json.dumps({
        "runtimeOptions": {"tfm": "net8.0", "framework": {"name": "Microsoft.NETCore.App", "version": version.name}}
    }), encoding="utf-8")
    subprocess.run([str(runner), str(assembly), str(root)], check=True)

SHIM = r"""
using System;
using System.Text.Json;
namespace UnityEngine {
    public class GameObject {}
    public class ScriptableObject {}
    public struct Vector2Int {
        public int x,y;
        public Vector2Int(int x,int y) { this.x=x; this.y=y; }
        public static Vector2Int one => new Vector2Int(1,1);
    }
    public class TextAreaAttribute : Attribute {}
    public class CreateAssetMenuAttribute : Attribute { public string menuName; public string fileName; }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string text) {} }
    public static class JsonUtility {
        private static readonly JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields=true };
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, options);
        public static string ToJson(object value, bool prettyPrint=false) => JsonSerializer.Serialize(value, value.GetType(), options);
    }
}
"""
HARNESS = r"""
using System;
using System.IO;
using System.Linq;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using UnityEngine;
public static class PolicyHarness {
    private static int count;
    private static void Check(bool success,string name) { if (!success) throw new Exception(name); count++; }
    private static GameEventRuntime Runtime(params DialogueDefinition[] dialogues) {
        return new GameEventRuntime(new GameEventCatalog(new[] { new GameEventDatabaseDefinition {
            events=new[] { new GameEventDefinition { id="chapter1.first_account" } }, dialogues=dialogues } }));
    }
    public static int Main(string[] args) {
        var npc=new NpcDefinition { id="ambient_fixture", ambientOnly=true,
            ambientSchedule=new AmbientNpcScheduleDefinition { enabled=true, phaseCounterKey="phase",
                stops=new[] { new AmbientNpcStopDefinition { x=1 },new AmbientNpcStopDefinition { x=2 },new AmbientNpcStopDefinition { x=3 } } } };
        var runtime=Runtime();
        foreach (string id in new[] { "stella","cinna_cirra_orientation","cinna_cirra_trial","orio","katrina","receptionist" }) {
            npc.id=id; Check(!AmbientNpcPolicy.TryGetStop(npc,runtime,out _),"main actor protected "+id);
        }
        npc.id="ambient_fixture";
        foreach (int phase in new[] { -1,int.MinValue,int.MaxValue }) {
            runtime.SetCounter("phase",phase);
            var restored=Runtime(); Check(restored.ImportStateJson(runtime.ExportStateJson()),"restore");
            Check(AmbientNpcPolicy.TryGetStop(npc,restored,out var stop),"schedule phase");
            Check(stop.x==(phase==-1 ? 3 : 2),"normalized phase");
        }
        npc.ambientSchedule.enabled=false;
        Check(!AmbientNpcPolicy.TryGetStop(npc,runtime,out _),"disabled schedule");
        npc.ambientSchedule.enabled=true; npc.dialogue=new[] { new NpcDialogueDefinition { questId="quest" } };
        Check(!AmbientNpcPolicy.TryGetStop(npc,runtime,out _),"quest schedule rejected");
        npc.dialogue=Array.Empty<NpcDialogueDefinition>();
        var node=new DialogueNodeDefinition { id="a" };
        var neutral=new DialogueDefinition { id="neutral",startNodeId="a",nodes=new[] { node },
            metadata=new[] { new GameMetadataEntry { key="ambient" } } };
        Check(AmbientNpcPolicy.IsQuestNeutral(neutral),"ambient metadata");
        node.onEnterActions=new[] { new GameActionDefinition { type="SetFlag",key="quest" } };
        Check(!AmbientNpcPolicy.IsQuestNeutral(neutral),"node side effects");
        node.onEnterActions=Array.Empty<GameActionDefinition>();
        node.choices=new[] { new DialogueChoiceDefinition { actions=new[] { new GameActionDefinition { type="CompleteQuest" } } } };
        Check(!AmbientNpcPolicy.IsQuestNeutral(neutral),"choice side effects");
        node.choices=Array.Empty<DialogueChoiceDefinition>();
        foreach (string key in new[] { "spellReward","keyItemReward","unknownCustomAction" }) {
            neutral.metadata=new[] { new GameMetadataEntry { key=key } };
            Check(!AmbientNpcPolicy.IsQuestNeutral(neutral),"terminal metadata "+key);
        }
        neutral.metadata=Array.Empty<GameMetadataEntry>();
        var other=new DialogueDefinition { id="other",startNodeId="a",nodes=new[] { node } };
        var unsafeLine=new DialogueDefinition { id="unsafe",startNodeId="a",nodes=new[] { node },
            metadata=new[] { new GameMetadataEntry { key="spellReward" } } };
        runtime=Runtime(neutral,other,unsafeLine);
        npc.dialogueIds=new[] { "unsafe","neutral" };
        npc.dialogue=new[] { new NpcDialogueDefinition { dialogueId="other",weight=3 } };
        Check(AmbientNpcPolicy.SelectDialogue(npc,runtime,0)=="neutral","weighted low boundary");
        Check(AmbientNpcPolicy.SelectDialogue(npc,runtime,0.3)=="other","weighted selection");
        Check(AmbientNpcPolicy.SelectDialogue(npc,runtime,1)=="other","weighted high boundary");
        string authored=File.ReadAllText(Path.Combine(args[0],"Lumenfall/Assets/Resources/GameEvents/quest_npc_dialogue.json"));
        Check(GameEventCatalog.TryParseJson(authored,"authored",out var database,out _),"shipped dialogue parse");
        foreach (var line in database.dialogues.Where(d=>d.id.StartsWith("ambient_",StringComparison.Ordinal)))
            Check(AmbientNpcPolicy.IsQuestNeutral(line),"shipped neutral "+line.id);
        var gate=new PortalContentAsset { reserved=true,allowedFlag="gate" };
        runtime.SetFlag("gate",true);
        Check(!OptionalTravelPolicy.GateAllows(gate,runtime),"reserved disabled");
        gate.allowReservedTravel=true;
        Check(OptionalTravelPolicy.GateAllows(gate,runtime),"reserved explicit optin");
        gate.requiredFlags=new[] { "extra" };
        Check(!OptionalTravelPolicy.GateAllows(gate,runtime),"missing secondary gate flag");
        runtime.SetFlag("extra",true);
        Check(OptionalTravelPolicy.GateAllows(gate,runtime),"all gate flags");
        gate.allowedFlag=""; Check(!OptionalTravelPolicy.GateAllows(gate,runtime),"blank gate flag");
        gate.allowedFlag="gate"; runtime.SetFlag("gate",false);
        Check(!OptionalTravelPolicy.GateAllows(gate,runtime),"false gate flag");
        var pair=OptionalTravelPolicy.DisabledChapterOneWaypoints();
        runtime=Runtime();
        runtime.SetFlag("chapter1.first_account",true);
        runtime.SetFlag(OptionalTravelPolicy.VisitFlag("D03"),true);
        runtime.SetFlag(OptionalTravelPolicy.VisitFlag("W07"),true);
        Check(!OptionalTravelPolicy.WaypointAllows(pair[0],pair[1],runtime),"waypoint disabled");
        pair[0].enabled=pair[1].enabled=true;
        Check(!OptionalTravelPolicy.WaypointAllows(pair[0],pair[1],runtime),"flag alone is not introduction");
        runtime.TriggerEvent("chapter1.first_account");
        Check(OptionalTravelPolicy.WaypointAllows(pair[0],pair[1],runtime),"both visits with real introduction");
        var savedRuntime=Runtime(); Check(savedRuntime.ImportStateJson(runtime.ExportStateJson()),"waypoint restore");
        Check(OptionalTravelPolicy.WaypointAllows(pair[0],pair[1],savedRuntime),"saved activation");
        savedRuntime.SetFlag(OptionalTravelPolicy.VisitFlag("W07"),false);
        Check(!OptionalTravelPolicy.WaypointAllows(pair[0],pair[1],savedRuntime),"single visit");
        savedRuntime.SetFlag(OptionalTravelPolicy.VisitFlag("W07"),true);
        pair[1].regionId="D08";
        Check(!OptionalTravelPolicy.WaypointAllows(pair[0],pair[1],savedRuntime),"suite shortcut rejected");
        var map=new MapDefinition { regions=new[] { new MapRegionDefinition { id="D03",walkable=true,
            rect=new MapRectDefinition { x=18,y=2,width=6,height=3 } } } };
        Check(OptionalTravelPolicy.RegionContains(map,"D03",new Vector2Int(18,2)),"region lower boundary");
        Check(!OptionalTravelPolicy.RegionContains(map,"D03",new Vector2Int(24,2)),"region upper boundary");
        Console.WriteLine("PASS: "+count+" assertions using current ambient/travel/GameEvent C# source; Unity integration unexecuted.");
        return 0;
    }
}
"""
if __name__ == "__main__":
    main()
