using System.Collections.Generic;
using System.Linq;
using Erasmus.ARToolkit.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace Erasmus.ARToolkit.Tests
{
    public class StoryRunnerTests
    {
        readonly List<Object> objects = new List<Object>();
        StoryCharacter ayse;
        StoryPoint door;

        [SetUp]
        public void SetUp()
        {
            ayse = new GameObject("Ayse").AddComponent<StoryCharacter>();
            door = new GameObject("Door").AddComponent<StoryPoint>();
            objects.Add(ayse.gameObject);
            objects.Add(door.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in objects)
            {
                Object.DestroyImmediate(obj);
            }

            objects.Clear();
        }

        [Test]
        public void AutoSteps_FollowEachOther_AfterTheirTime()
        {
            var runner = new StoryRunner(new[]
            {
                new StoryStep(ayse, StoryAction.Say, "Hi", seconds: 1f),
                new StoryStep(null, StoryAction.Wait, seconds: 2f),
            });
            var started = new List<int>();
            var finished = false;
            runner.StepStarted += (i, _) => started.Add(i);
            runner.Finished += () => finished = true;
            runner.Start();
            runner.Tick(0.5f, true, false, 10f);
            Assert.AreEqual(0, runner.Index, "A line stays for its time.");
            runner.Tick(0.6f, true, false, 10f);
            Assert.AreEqual(1, runner.Index);
            runner.Tick(2.1f, true, false, 10f);
            Assert.IsTrue(finished);
            Assert.AreEqual(StoryPhase.Finished, runner.Phase);
            CollectionAssert.AreEqual(new[] { 0, 1 }, started);
        }

        [Test]
        public void TapStep_WaitsForATap_AndATapEndsALineEarly()
        {
            var runner = new StoryRunner(new[]
            {
                new StoryStep(ayse, StoryAction.Say, "Tap me", StoryAdvance.Tap, seconds: 5f),
                new StoryStep(ayse, StoryAction.TurnToUser, advance: StoryAdvance.Tap),
                new StoryStep(null, StoryAction.Wait),
            });
            runner.Start();
            Assert.IsTrue(runner.WantsTap);
            runner.Tick(0.1f, true, true, 10f);
            Assert.AreEqual(1, runner.Index, "A tap finishes the line.");
            runner.Tick(0.1f, false, false, 10f);
            Assert.AreEqual(StoryPhase.Acting, runner.Phase, "The turn is not done yet.");
            runner.Tick(0.1f, true, false, 10f);
            Assert.AreEqual(StoryPhase.WaitingForTap, runner.Phase);
            runner.Tick(5f, true, false, 10f);
            Assert.AreEqual(1, runner.Index, "Without a tap it waits.");
            runner.Tick(0.1f, true, true, 10f);
            Assert.AreEqual(2, runner.Index);
        }

        [Test]
        public void UserNearStep_WaitsUntilTheUserComesClose()
        {
            var runner = new StoryRunner(new[]
            {
                new StoryStep(ayse, StoryAction.TurnToUser, advance: StoryAdvance.UserNear, nearMeters: 1.5f),
                new StoryStep(null, StoryAction.Wait),
            });
            runner.Start();
            runner.Tick(0.1f, true, false, 4f);
            Assert.AreEqual(StoryPhase.WaitingForUser, runner.Phase);
            runner.Tick(0.1f, true, false, 1.2f);
            Assert.AreEqual(1, runner.Index);
        }

        [Test]
        public void WalkStep_EndsWhenTheWalkArrives()
        {
            var runner = new StoryRunner(new[]
            {
                new StoryStep(ayse, StoryAction.WalkTo, point: door),
                new StoryStep(null, StoryAction.Wait),
            });
            runner.Start();
            runner.Tick(3f, false, false, 10f);
            Assert.AreEqual(0, runner.Index);
            runner.Tick(0.1f, true, false, 10f);
            Assert.AreEqual(1, runner.Index);
        }

        [Test]
        public void StepsMissingTheirCharacterOrPoint_AreLeftOut()
        {
            var runner = new StoryRunner(new[]
            {
                new StoryStep(null, StoryAction.Say, "Nobody says this"),
                new StoryStep(ayse, StoryAction.WalkTo),
                new StoryStep(ayse, StoryAction.Say, "Hello"),
            });
            runner.Start();
            Assert.AreEqual(2, runner.Index);
            CollectionAssert.AreEqual(new[] { 0, 1 }, runner.Skipped);
        }

        [Test]
        public void Restart_BeginsAgain()
        {
            var runner = new StoryRunner(new[] { new StoryStep(null, StoryAction.Wait), new StoryStep(null, StoryAction.Wait) });
            runner.Start();
            runner.Tick(0.1f, true, false, 10f);
            runner.Tick(0.1f, true, false, 10f);
            Assert.AreEqual(StoryPhase.Finished, runner.Phase);
            runner.Start();
            Assert.AreEqual(0, runner.Index);
            Assert.AreEqual(StoryPhase.Acting, runner.Phase);
        }

        [Test]
        public void ReadingTime_GrowsWithTheLine_UpToALimit()
        {
            Assert.Less(StoryTiming.ReadingSeconds("Hi"), StoryTiming.ReadingSeconds("A much longer line to read."));
            Assert.AreEqual(8f, StoryTiming.ReadingSeconds(new string('a', 500)));
            Assert.AreEqual("Ayse: Hello", StoryText.Subtitle("Ayse", " Hello "));
        }
    }

    public class StoryNavMeshTests
    {
        [Test]
        public void Sources_FloorIsWalkable_ObstaclesAndWallsAreNot()
        {
            var floor = new Bounds(new Vector3(1f, 0f, 2f), new Vector3(6f, 0f, 4f));
            var table = new ObstacleVolume(1, new Pose(new Vector3(1f, 0f, 2f), Quaternion.identity), new Vector3(1.2f, 0.75f, 0.7f),
                ObstacleKind.Table, 1f, 1f);
            var wall = new WallSegment(new Vector2(-2f, 4f), new Vector2(4f, 4f), 0f, 2.4f, 1f, true);
            var sources = StoryNavMesh.Sources(floor, new[] { table }, new[] { wall });

            Assert.AreEqual(3, sources.Count);
            Assert.AreEqual(StoryNavMesh.Walkable, sources[0].area);
            Assert.AreEqual(new Vector3(6f, 0.02f, 4f), sources[0].size);
            Assert.AreEqual(StoryNavMesh.NotWalkable, sources[1].area);
            Assert.AreEqual(0.375f, sources[1].transform.GetColumn(3).y, 0.0001f, "The table box stands on the floor.");
            Assert.AreEqual(StoryNavMesh.NotWalkable, sources[2].area);
            Assert.AreEqual(6f, sources[2].size.z, 0.0001f, "The wall box is as long as the wall.");
            Assert.AreEqual(NavMeshBuildSourceShape.Box, sources[2].shape);
        }

        [Test]
        public void Sources_WithoutAScan_AreJustTheFloor()
        {
            var sources = StoryNavMesh.Sources(new Bounds(Vector3.zero, new Vector3(10f, 0f, 10f)), null, null);
            Assert.AreEqual(1, sources.Count);
        }
    }

    public class StoryBuilderTests
    {
        static Material NoMaterial(string name, Color color) => null;

        [Test]
        public void CreateStory_HasCharactersPointsAndPlayableSteps()
        {
            var stage = StoryBuilder.CreateStory(PlacementMode.TapToPlace, NoMaterial);
            try
            {
                Assert.AreEqual(2, stage.GetComponentsInChildren<StoryCharacter>().Length);
                Assert.AreEqual(2, stage.GetComponentsInChildren<StoryPoint>().Length);
                Assert.AreEqual(5, stage.Steps.Count);
                Assert.IsTrue(stage.Steps.All(s => s.IsPlayable));
                Assert.IsTrue(stage.GetComponentsInChildren<StoryCharacter>().All(c => c.GetComponent<Collider>() != null),
                    "Characters can be tapped for their card.");
            }
            finally
            {
                Object.DestroyImmediate(stage.gameObject);
            }
        }

        [Test]
        public void AddCharacterAndPoint_GoUnderTheStory()
        {
            var stage = StoryBuilder.CreateStory(PlacementMode.AtAppStart, NoMaterial);
            try
            {
                var character = StoryBuilder.AddCharacter(stage, "Mert", Color.red, null, 1.4f, NoMaterial);
                var point = StoryBuilder.AddPoint(stage, "Board");
                Assert.AreSame(stage.transform, character.transform.parent);
                Assert.AreEqual("Mert", character.DisplayName);
                Assert.AreEqual(1.4f, character.WalkSpeed, 0.0001f);
                Assert.AreSame(stage.transform, point.transform.parent);
                Assert.AreSame(stage, StoryBuilder.FindStage(point.gameObject));
            }
            finally
            {
                Object.DestroyImmediate(stage.gameObject);
            }
        }
    }
}
