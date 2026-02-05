using System;
using System.Collections.Generic;
using Core;
using UnityEngine;

namespace Tests
{
    /// <summary>
    /// Basic test to verify the fact system functionality.
    /// Run this from Unity console or as a unit test.
    /// </summary>
    public static class FactSystemTest
    {
        public static void RunTests()
        {
            Debug.Log("=== Starting Fact System Tests ===");
            
            TestFactCreation();
            TestFactEmission();
            TestFactPruning();
            TestFactReporting();
            
            Debug.Log("=== All Fact System Tests Completed ===");
        }

        private static void TestFactCreation()
        {
            Debug.Log("[Test] FactCreation: Creating a fact instance...");
            
            var fact = FactInstanceFactory.Create(
                type: "TestFact",
                day: 10,
                nodeId: "N1",
                anomalyId: "AN_001",
                severity: 3,
                tags: new List<string> { "test", "example" },
                payload: new Dictionary<string, object> 
                { 
                    { "key1", "value1" },
                    { "key2", 123 }
                },
                source: "TestSource"
            );

            Assert(fact != null, "Fact should not be null");
            Assert(!string.IsNullOrEmpty(fact.FactId), "Fact ID should be generated");
            Assert(fact.Type == "TestFact", "Fact type should match");
            Assert(fact.Day == 10, "Fact day should match");
            Assert(fact.NodeId == "N1", "Fact nodeId should match");
            Assert(fact.Severity == 3, "Fact severity should match");
            Assert(fact.Tags.Count == 2, "Fact should have 2 tags");
            Assert(fact.Payload.Count == 2, "Fact should have 2 payload entries");
            Assert(fact.Reported == false, "Fact should start as not reported");

            Debug.Log("[Test] FactCreation: PASSED");
        }

        private static void TestFactEmission()
        {
            Debug.Log("[Test] FactEmission: Testing fact emission...");
            
            var state = CreateTestGameState();
            int initialCount = state.FactSystem.Facts.Count;

            Sim.EmitFact(
                state,
                type: "AnomalySpawned",
                nodeId: "N1",
                anomalyId: "AN_001",
                severity: 4,
                tags: new List<string> { "anomaly", "spawn" },
                payload: new Dictionary<string, object> 
                { 
                    { "nodeName", "TestNode" },
                    { "anomalyClass", "Keter" }
                },
                source: "TestEmission"
            );

            Assert(state.FactSystem.Facts.Count == initialCount + 1, "Should have one more fact");
            var emittedFact = state.FactSystem.Facts[state.FactSystem.Facts.Count - 1];
            Assert(emittedFact.Type == "AnomalySpawned", "Emitted fact type should match");
            Assert(emittedFact.Severity == 4, "Emitted fact severity should match");

            Debug.Log("[Test] FactEmission: PASSED");
        }

        private static void TestFactPruning()
        {
            Debug.Log("[Test] FactPruning: Testing fact retention...");
            
            var state = CreateTestGameState();
            state.Day = 100;
            state.FactSystem.RetentionDays = 60;

            // Add old facts (should be pruned)
            for (int i = 0; i < 5; i++)
            {
                state.FactSystem.Facts.Add(FactInstanceFactory.Create(
                    type: "OldFact",
                    day: 30, // Created on day 30, which is 70 days before current day 100
                    severity: 1
                ));
            }

            // Add recent facts (should be kept)
            for (int i = 0; i < 3; i++)
            {
                state.FactSystem.Facts.Add(FactInstanceFactory.Create(
                    type: "RecentFact",
                    day: 95, // Created on day 95, which is 5 days before current day 100
                    severity: 1
                ));
            }

            Assert(state.FactSystem.Facts.Count == 8, "Should have 8 facts before pruning");

            Sim.PruneFacts(state);

            Assert(state.FactSystem.Facts.Count == 3, "Should have 3 facts after pruning (only recent ones)");
            foreach (var fact in state.FactSystem.Facts)
            {
                Assert(fact.Type == "RecentFact", "All remaining facts should be recent");
            }

            Debug.Log("[Test] FactPruning: PASSED");
        }

        private static void TestFactReporting()
        {
            Debug.Log("[Test] FactReporting: Testing fact reported status...");
            
            var state = CreateTestGameState();
            
            // Add unreported fact
            Sim.EmitFact(state, "TestFact", severity: 2);
            
            var fact = state.FactSystem.Facts[0];
            Assert(fact.Reported == false, "New fact should be unreported");
            
            // Mark as reported
            fact.Reported = true;
            Assert(fact.Reported == true, "Fact should be reported after marking");

            Debug.Log("[Test] FactReporting: PASSED");
        }

        private static GameState CreateTestGameState()
        {
            return new GameState
            {
                Day = 1,
                FactSystem = new FactState
                {
                    Facts = new List<FactInstance>(),
                    RetentionDays = 60
                },
                NewsLog = new List<NewsInstance>(),
                News = new List<string>(),
                Nodes = new List<NodeState>()
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                Debug.LogError($"[ASSERTION FAILED] {message}");
                throw new Exception($"Assertion failed: {message}");
            }
        }
    }
}
