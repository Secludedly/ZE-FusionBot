using FluentAssertions;
using PKHeX.Core;
using SysBot.Pokemon;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SysBot.Tests;

public class QueueTests
{
    [Fact]
    public void TestEnqueuePK8() => EnqueueTest<PK8>();

    [Fact]
    public void TestFavortism() => TestFavor<PK8>();

    [Fact]
    public void TestDuplicateUserWithDifferentUniqueIDs() => TestDuplicateQueueEntries<PK8>();

    [Fact]
    public void TestConcurrentDuplicateRequests() => TestConcurrentDuplicates<PK8>();

    private static void TestConcurrentDuplicates<T>() where T : PKM, new()
    {
        var hub = new PokeTradeHub<T>(new PokeTradeHubConfig());
        var info = new TradeQueueInfo<T>(hub);

        Console.WriteLine("=== Testing Concurrent Duplicate Queue Entry Bug ===");
        Console.WriteLine("Simulating the RACE CONDITION from Discord command flow");
        Console.WriteLine();

        const ulong testUserId = 987654321;
        const string testUserName = "ConcurrentUser";

        // Simulate what happens in Discord: multiple commands execute in parallel
        // Command 1 and Command 2 both check IsUserInQueue before either adds to queue
        Console.WriteLine("Step 1: Both commands check if user is in queue (both see 'false')");
        bool check1 = !info.IsUserInQueue(testUserId);
        bool check2 = !info.IsUserInQueue(testUserId);
        Console.WriteLine($"  Command 1 check: {check1} (proceed)");
        Console.WriteLine($"  Command 2 check: {check2} (proceed)");
        check1.Should().BeTrue("First command sees user not in queue");
        check2.Should().BeTrue("Second command ALSO sees user not in queue (RACE CONDITION!)");
        Console.WriteLine();

        // Now both commands proceed to add to queue concurrently
        Console.WriteLine("Step 2: Both commands try to add to queue with different UniqueTradeIDs");

        var trade1 = CreateTradeWithUniqueId<T>(info, testUserId, testUserName, 5001);
        var trade2 = CreateTradeWithUniqueId<T>(info, testUserId, testUserName, 5002);

        // Simulate concurrent execution using Task.Run (like in ProcessTradeAsync)
        var task1 = Task.Run(() =>
        {
            var result = info.AddToTradeQueue(trade1, testUserId, allowMultiple: false, sudo: false);
            Console.WriteLine($"  Task 1: UniqueTradeID={trade1.UniqueTradeID}, Result={result}");
            return result;
        });

        var task2 = Task.Run(() =>
        {
            var result = info.AddToTradeQueue(trade2, testUserId, allowMultiple: false, sudo: false);
            Console.WriteLine($"  Task 2: UniqueTradeID={trade2.UniqueTradeID}, Result={result}");
            return result;
        });

        // Wait for both tasks to complete
        Task.WaitAll(task1, task2);

        var result1 = task1.Result;
        var result2 = task2.Result;

        Console.WriteLine();
        Console.WriteLine($"Final Queue Count: {info.Count}");
        Console.WriteLine($"Expected: 1 (only first should be added, second should be blocked)");
        Console.WriteLine();

        // At least one should be Added
        bool oneAdded = result1 == QueueResultAdd.Added || result2 == QueueResultAdd.Added;
        oneAdded.Should().BeTrue("At least one command should succeed");

        // At least one should be blocked as AlreadyInQueue
        bool oneBlocked = result1 == QueueResultAdd.AlreadyInQueue || result2 == QueueResultAdd.AlreadyInQueue;
        oneBlocked.Should().BeTrue("At least one command should be blocked");

        // THE KEY ASSERTION: Queue should only have 1 entry, not 2!
        info.Count.Should().Be(1, "User should only be in queue ONCE, even with concurrent requests");

        Console.WriteLine("=== Test Complete ===");
    }

    private static void TestDuplicateQueueEntries<T>() where T : PKM, new()
    {
        var hub = new PokeTradeHub<T>(new PokeTradeHubConfig());
        var info = new TradeQueueInfo<T>(hub);
        var queue = info.Hub.Queues.GetQueue(PokeRoutineType.LinkTrade);

        Console.WriteLine("=== Testing Duplicate Queue Entry Bug ===");
        Console.WriteLine("Simulating same user making multiple trade requests (like in Discord)");
        Console.WriteLine();

        const ulong testUserId = 123456789;
        const string testUserName = "TestUser";

        // Simulate first trade request from user (with UniqueTradeID 1001)
        var trade1 = CreateTradeWithUniqueId<T>(info, testUserId, testUserName, 1001);
        var result1 = info.AddToTradeQueue(trade1, testUserId, allowMultiple: false, sudo: false);

        Console.WriteLine($"Request 1: UserID={testUserId}, UniqueTradeID={trade1.UniqueTradeID}");
        Console.WriteLine($"  Result: {result1}");
        Console.WriteLine($"  Queue Count: {info.Count}");
        Console.WriteLine($"  Expected: Added");
        result1.Should().Be(QueueResultAdd.Added, "First request should be added");
        info.Count.Should().Be(1, "Queue should have 1 entry after first request");
        Console.WriteLine();

        // Small delay to simulate time between requests
        Thread.Sleep(50);

        // Simulate second trade request from SAME user (with different UniqueTradeID 2002)
        // This simulates what happens in Discord when GenerateUniqueTradeID() is called again
        var trade2 = CreateTradeWithUniqueId<T>(info, testUserId, testUserName, 2002);
        var result2 = info.AddToTradeQueue(trade2, testUserId, allowMultiple: false, sudo: false);

        Console.WriteLine($"Request 2: UserID={testUserId}, UniqueTradeID={trade2.UniqueTradeID}");
        Console.WriteLine($"  Result: {result2}");
        Console.WriteLine($"  Queue Count: {info.Count}");
        Console.WriteLine($"  Expected: AlreadyInQueue (user should be blocked!)");

        // THIS IS THE BUG - the user should NOT be able to queue again!
        result2.Should().Be(QueueResultAdd.AlreadyInQueue, "Same user should NOT be allowed to queue multiple times");
        info.Count.Should().Be(1, "Queue should still have only 1 entry - duplicate should be blocked");

        Console.WriteLine();

        // Third attempt - verify it's still blocked
        Thread.Sleep(50);
        var trade3 = CreateTradeWithUniqueId<T>(info, testUserId, testUserName, 3003);
        var result3 = info.AddToTradeQueue(trade3, testUserId, allowMultiple: false, sudo: false);

        Console.WriteLine($"Request 3: UserID={testUserId}, UniqueTradeID={trade3.UniqueTradeID}");
        Console.WriteLine($"  Result: {result3}");
        Console.WriteLine($"  Queue Count: {info.Count}");
        Console.WriteLine($"  Expected: AlreadyInQueue");

        result3.Should().Be(QueueResultAdd.AlreadyInQueue, "User should still be blocked on third attempt");
        info.Count.Should().Be(1, "Queue should still have only 1 entry");

        Console.WriteLine();
        Console.WriteLine("=== Test Complete ===");
    }

    private static TradeEntry<T> CreateTradeWithUniqueId<T>(TradeQueueInfo<T> info, ulong userId, string userName, int uniqueTradeId) where T : PKM, new()
    {
        var trainerInfo = new PokeTradeTrainerInfo(userName, userId);
        var notifier = new PokeTradeLogNotifier<T>();
        var detail = new PokeTradeDetail<T>(
            new T { Species = 25 }, // Pikachu
            trainerInfo,
            notifier,
            PokeTradeType.Specific,
            1234, // trade code
            false, // not favored
            null, // lgcode
            1, // batch trade number
            1, // total batch trades
            false, // not mystery egg
            uniqueTradeId, // THE UNIQUE ID THAT CHANGES EACH REQUEST
            false, // ignoreAutoOT
            false  // setEdited
        );

        var trade = new TradeEntry<T>(detail, userId, PokeRoutineType.LinkTrade, userName, uniqueTradeId);

        // Set up the OnFinish callback like in production code
        trade.Trade.Notifier.OnFinish = r => info.Remove(trade);

        return trade;
    }

    private static void EnqueueTest<T>() where T : PKM, new()
    {
        var hub = new PokeTradeHub<T>(new PokeTradeHubConfig());
        var info = new TradeQueueInfo<T>(hub);
        var queue = info.Hub.Queues.GetQueue(PokeRoutineType.LinkTrade);

        var t1 = GetTestTrade(info, 1);
        var t2 = GetTestTrade(info, 2);
        var t3 = GetTestTrade(info, 3);
        var s = GetTestTrade(info, 4);

        var executor = new MockExecutor<T>(new PokeBotState());

        // Enqueue a bunch
        var r1 = info.AddToTradeQueue(t1, t1.UserID);
        r1.Should().Be(QueueResultAdd.Added);

        var r2 = info.AddToTradeQueue(t2, t2.UserID);
        r2.Should().Be(QueueResultAdd.Added);

        var r3 = info.AddToTradeQueue(t3, t3.UserID);
        r3.Should().Be(QueueResultAdd.Added);

        // Test adding same user ID without sudo - should fail
        var id = t1.UserID;
        var sr = info.AddToTradeQueue(s, id);
        sr.Should().Be(QueueResultAdd.AlreadyInQueue);

        // Sudo add with unique ID - should succeed
        sr = info.AddToTradeQueue(s, s.UserID, allowMultiple: false, sudo: true);
        sr.Should().Be(QueueResultAdd.Added);

        var dequeue = queue.TryDequeue(out var first, out uint priority);
        priority.Should().Be(PokeTradePriorities.Tier1); // sudo
        dequeue.Should().BeTrue();
        ReferenceEquals(first, s.Trade).Should().BeTrue();

        first.Notifier.TradeInitialize(executor, first);
        first.Notifier.TradeSearching(executor, first);
        first.Notifier.TradeFinished(executor, first, new T { Species = 777 });

        // Verify counts after sudo trade completion
        var count = info.UserCount(z => z.Type == PokeRoutineType.LinkTrade);
        count.Should().Be(3);
        queue.Count.Should().Be(3);

        dequeue = queue.TryDequeue(out var second, out priority);
        priority.Should().Be(PokeTradePriorities.TierFree); // sudo
        dequeue.Should().BeTrue();
        ReferenceEquals(second, t1.Trade).Should().BeTrue();

        second.Notifier.TradeInitialize(executor, second);
        second.Notifier.TradeSearching(executor, second);
        second.Notifier.TradeCanceled(executor, second, PokeTradeResult.TrainerTooSlow);

        // Verify final counts
        count = info.UserCount(z => z.Type == PokeRoutineType.LinkTrade);
        count.Should().Be(2);
        queue.Count.Should().Be(2);
    }

    private class MockExecutor<T>(PokeBotState Config) : PokeRoutineExecutor<T>(Config)
        where T : PKM, new()
    {
        public override Task MainLoop(CancellationToken token) => Task.CompletedTask;

        public override void SoftStop()
        { }

        public override Task HardStop() => Task.CompletedTask;

        public override Task<T> ReadPokemon(ulong offset, CancellationToken token) => Task.Run(() => new T(), token);

        public override Task<T> ReadPokemon(ulong offset, int size, CancellationToken token) => Task.Run(() => new T(), token);

        public override Task<T> ReadPokemonPointer(IEnumerable<long> jumps, int size, CancellationToken token) => Task.Run(() => new T(), token);

        public override Task<T> ReadBoxPokemon(int box, int slot, CancellationToken token) => Task.Run(() => new T(), token);

        public override Task RebootAndStop(CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }

    private static TradeEntry<T> GetTestTrade<T>(TradeQueueInfo<T> info, int tag, bool favor = false) where T : PKM, new()
    {
        var trade = GetTestTrade<T>(tag, favor);
        trade.Trade.Notifier.OnFinish = r => RemoveAndCheck(info, trade, r);
        return trade;
    }

    private static void RemoveAndCheck<T>(TradeQueueInfo<T> info, TradeEntry<T> trade, PokeRoutineExecutorBase routine) where T : PKM, new()
    {
        var result = info.Remove(trade);
        result.Should().BeTrue();
        routine.Should().NotBeNull();
    }

    private static TradeEntry<T> GetTestTrade<T>(int tag, bool favor) where T : PKM, new()
    {
        var d3 = new PokeTradeDetail<T>(new T { Species = (ushort)tag }, new PokeTradeTrainerInfo($"{(favor ? "*" : "")}Test {tag}"), new PokeTradeLogNotifier<T>(), PokeTradeType.Specific, tag, favor);
        return new TradeEntry<T>(d3, (ulong)tag, PokeRoutineType.LinkTrade, $"Test Trade {tag}", 12345);
    }

    private static void TestFavor<T>() where T : PKM, new()
    {
        var settings = new PokeTradeHubConfig();
        settings.Queues.MaxQueueCount = 200; // Increase to accommodate all test users
        var hub = new PokeTradeHub<T>(settings);
        var info = new TradeQueueInfo<T>(hub);
        var queue = info.Hub.Queues.GetQueue(PokeRoutineType.LinkTrade);

        const int count = 100;

        // Enqueue a bunch of regular users
        for (int i = 0; i < count; i++)
        {
            var s = GetTestTrade(info, i + 1);
            var r = info.AddToTradeQueue(s, s.UserID);
            r.Should().Be(QueueResultAdd.Added);
        }

        queue.Count.Should().Be(count);

        // Configure favoritism: 40% skip means priority users will skip 40% of regular users
        var f = settings.Favoritism;
        f.EnableFavoritism = true;
        f.SkipPercentage = 40;
        f.MinimumRegularUsersFirst = 3;

        // Enqueue some priority users
        for (int i = 0; i < count / 10; i++)
        {
            var s = GetTestTrade(info, count + i + 1, true);
            var r = info.AddToTradeQueue(s, s.UserID);
            r.Should().Be(QueueResultAdd.Added);
        }

        // With 100 regular users and 40% skip, priority users skip 40 users, so 60 remain ahead
        int expectedPosition = (int)Math.Ceiling(count * ((100 - f.SkipPercentage) / 100.0));
        for (int i = 0; i < expectedPosition; i++)
        {
            queue.TryDequeue(out var detail, out _);
            detail.IsFavored.Should().Be(false);
        }

        // Next user should be a priority user
        {
            queue.TryDequeue(out var detail, out _);
            detail.IsFavored.Should().Be(true);
        }
    }
}
