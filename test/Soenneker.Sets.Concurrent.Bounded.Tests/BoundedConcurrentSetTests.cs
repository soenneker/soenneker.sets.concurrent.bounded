using System;
using System.Linq;
using AwesomeAssertions;
using Soenneker.Tests.Unit;
using Xunit;

namespace Soenneker.Sets.Concurrent.Bounded.Tests;

public sealed class BoundedConcurrentSetTests : UnitTest
{
    [Fact]
    public void Constructor_sets_MaxSize()
    {
        var set = new BoundedConcurrentSet<int>(42);

        set.MaxSize.Should().Be(42);
        set.ApproxCount.Should().Be(0);
    }

    [Fact]
    public void TryAdd_adds_value_and_returns_true()
    {
        var set = new BoundedConcurrentSet<int>(10);

        bool added = set.TryAdd(1);

        added.Should().BeTrue();
        set.ApproxCount.Should().Be(1);
        set.Contains(1).Should().BeTrue();
    }

    [Fact]
    public void TryAdd_duplicate_returns_false()
    {
        var set = new BoundedConcurrentSet<int>(10);
        set.TryAdd(1);

        bool addedAgain = set.TryAdd(1);

        addedAgain.Should().BeFalse();
        set.ApproxCount.Should().Be(1);
    }

    [Fact]
    public void Contains_returns_true_when_value_exists()
    {
        var set = new BoundedConcurrentSet<string>(10);
        set.TryAdd("foo");

        set.Contains("foo").Should().BeTrue();
    }

    [Fact]
    public void Contains_returns_false_when_value_missing()
    {
        var set = new BoundedConcurrentSet<string>(10);

        set.Contains("missing").Should().BeFalse();
    }

    [Fact]
    public void TryRemove_removes_value_and_returns_true()
    {
        var set = new BoundedConcurrentSet<int>(10);
        set.TryAdd(1);

        bool removed = set.TryRemove(1);

        removed.Should().BeTrue();
        set.ApproxCount.Should().Be(0);
        set.Contains(1).Should().BeFalse();
    }

    [Fact]
    public void TryRemove_missing_value_returns_false()
    {
        var set = new BoundedConcurrentSet<int>(10);

        set.TryRemove(1).Should().BeFalse();
    }

    [Fact]
    public void ToArray_returns_snapshot_of_values()
    {
        var set = new BoundedConcurrentSet<int>(10);
        set.TryAdd(1);
        set.TryAdd(2);

        int[] arr = set.ToArray();

        arr.Should().HaveCount(2);
        arr.Should().Contain(1);
        arr.Should().Contain(2);
    }

    [Fact]
    public void Values_contains_added_items()
    {
        var set = new BoundedConcurrentSet<int>(10);
        set.TryAdd(1);
        set.TryAdd(2);

        var values = set.Values.ToList();

        values.Should().HaveCount(2);
        values.Should().Contain(1);
        values.Should().Contain(2);
    }

    [Fact]
    public void Constructor_maxSize_zero_throws()
    {
        Action act = () => _ = new BoundedConcurrentSet<int>(0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("maxSize");
    }

    [Fact]
    public void Constructor_maxSize_negative_throws()
    {
        Action act = () => _ = new BoundedConcurrentSet<int>(-1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("maxSize");
    }

    [Fact]
    public void Constructor_trimBatchSize_zero_throws()
    {
        Action act = () => _ = new BoundedConcurrentSet<int>(10, 0, trimBatchSize: 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("trimBatchSize");
    }

    [Fact]
    public void Constructor_trimStartOveragePercent_negative_throws()
    {
        Action act = () => _ = new BoundedConcurrentSet<int>(10, trimStartOveragePercent: -1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("trimStartOveragePercent");
    }

    [Fact]
    public void Constructor_maxTrimWorkPerCall_zero_throws()
    {
        Action act = () => _ = new BoundedConcurrentSet<int>(10, maxTrimWorkPerCall: 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("maxTrimWorkPerCall");
    }

    [Fact]
    public void Constructor_resyncAfterNoProgress_negative_throws()
    {
        Action act = () => _ = new BoundedConcurrentSet<int>(10, resyncAfterNoProgress: -1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("resyncAfterNoProgress");
    }

    [Fact]
    public void Constructor_queueOverageFactor_zero_throws()
    {
        Action act = () => _ = new BoundedConcurrentSet<int>(10, queueOverageFactor: 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("queueOverageFactor");
    }

    [Fact]
    public void Add_multiple_items_approx_count_matches()
    {
        var set = new BoundedConcurrentSet<int>(100);

        for (var i = 0; i < 10; i++)
            set.TryAdd(i);

        set.ApproxCount.Should().Be(10);
        set.ToArray().Should().HaveCount(10);
    }

    [Fact]
    public void Remove_then_add_same_value_succeeds()
    {
        var set = new BoundedConcurrentSet<int>(10);
        set.TryAdd(1);
        set.TryRemove(1);

        set.TryAdd(1).Should().BeTrue();
        set.Contains(1).Should().BeTrue();
        set.ApproxCount.Should().Be(1);
    }
}
