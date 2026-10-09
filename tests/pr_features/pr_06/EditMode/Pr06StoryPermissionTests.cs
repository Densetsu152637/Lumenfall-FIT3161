using System;
using System.Collections.Generic;
using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR06")]
    public sealed class Pr06StoryPermissionTests
    {
        private static GameEventRuntime Runtime()
        {
            var databases = new List<GameEventDatabaseDefinition>();
            foreach (string path in new[] { "GameEvents/chapter_one_story", "GameEvents/quest_npc_dialogue" })
            {
                var asset = Resources.Load<TextAsset>(path);
                Assert.That(asset, Is.Not.Null, path);
                Assert.That(GameEventCatalog.TryParseJson(asset.text, path, out var data, out string error), Is.True, error);
                databases.Add(data);
            }
            return new GameEventRuntime(new GameEventCatalog(databases));
        }
        private static void FinishReception(GameEventRuntime runtime)
        {
            var dialogue = runtime.StartDialogue("chapter1.reception_first");
            Assert.That(dialogue, Is.Not.Null);
            for (int i = 0; dialogue.IsActive && i < 30; i++) Assert.That(dialogue.Continue(), Is.True);
            Assert.That(dialogue.IsCompleted, Is.True);
        }
        [Test]
        public void IndependentDialogueGuardsMustBothReleaseBeforeReceptionOpens()
        {
            var runtime = Runtime();
            Func<string, bool> first = _ => false, second = _ => false;
            runtime.ChapterOne.AddDialoguePermission(first);
            runtime.ChapterOne.AddDialoguePermission(second);
            runtime.ChapterOne.RemoveDialoguePermission(first);
            Assert.That(runtime.StartDialogue("chapter1.reception_first"), Is.Null);
            runtime.ChapterOne.RemoveDialoguePermission(second);
            FinishReception(runtime);
            Assert.That(runtime.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.FindingFriends));
        }
        [Test]
        public void DuplicateTransitionRegistrationHasSingleOwnerAndCannotLeakAuthorization()
        {
            var runtime = Runtime();
            Func<string, bool> guard = _ => false;
            runtime.ChapterOne.AddTransitionPermission(guard);
            runtime.ChapterOne.AddTransitionPermission(guard);
            FinishReception(runtime);
            Assert.That(runtime.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.Arrival));
            runtime.ChapterOne.RemoveTransitionPermission(guard);
            Assert.That(runtime.TriggerEvent("chapter1.first_account").Succeeded, Is.False);
            FinishReception(runtime);
            Assert.That(runtime.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.FindingFriends));
        }
        [Test]
        public void ChapterDialogueGuardDoesNotBlockUnrelatedQuestConversations()
        {
            var runtime = Runtime();
            runtime.ChapterOne.AddDialoguePermission(_ => false);
            Assert.That(runtime.ChapterOne.CanStartDialogue("orio_patch_notes_dialogue"), Is.True);
            Assert.That(runtime.ChapterOne.SelectNpcDialogue("unknown-npc"), Is.Null);
            Assert.That(runtime.ChapterOne.SelectNpcDialogue("stella"), Is.EqualTo("chapter1.find_reception"));
            Assert.That(runtime.ChapterOne.CanStartDialogue(null), Is.True);
        }
    }
}
