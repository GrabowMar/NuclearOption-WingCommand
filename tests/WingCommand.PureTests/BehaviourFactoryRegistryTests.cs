using System;
using Xunit;

namespace WingCommand.PureTests
{
    public sealed class BehaviourFactoryRegistryTests
    {
        private const string Id = "example.patrol";

        [Fact]
        public void ReplacingActiveBehaviourInvalidatesExistingMemberAndConstructsOnce()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            var aircraft = new object();
            int oldCalls = 0, replacementCalls = 0;
            registry.Register(Id, context => { Assert.Same(aircraft, context); oldCalls++; return new object(); });
            var original = registry.Find(Id);
            object first = cache.GetOrCreate(Id, original, aircraft);
            Assert.Same(first, cache.GetOrCreate(Id, original, aircraft));

            registry.Register(Id, context => { Assert.Same(aircraft, context); replacementCalls++; return new object(); });
            var replacement = registry.Find(Id);
            Assert.False(cache.IsCurrent(Id, replacement));
            object second = cache.GetOrCreate(Id, replacement, aircraft);

            Assert.NotSame(first, second);
            Assert.True(cache.IsCurrent(Id, replacement));
            Assert.Same(second, cache.GetOrCreate(Id, replacement, aircraft));
            Assert.Equal(1, oldCalls);
            Assert.Equal(1, replacementCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RemovingAndRestoringTheSameDelegateStartsANewStateLifetime(bool clearAll)
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            Func<object, object> factory = _ => new object();
            registry.Register(Id, factory);
            object first = cache.GetOrCreate(Id, registry.Find(Id), null);

            if (clearAll) registry.Clear(); else Assert.True(registry.Remove(Id));
            Assert.False(cache.IsCurrent(Id, registry.Find(Id)));
            registry.Register(Id, factory);
            Assert.False(cache.IsCurrent(Id, registry.Find(Id)));
            Assert.NotSame(first, cache.GetOrCreate(Id, registry.Find(Id), null));
        }

        [Fact]
        public void RegisteringTheSameDelegateAgainAlsoInvalidatesItsState()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            Func<object, object> factory = _ => new object();
            registry.Register(Id, factory);
            object first = cache.GetOrCreate(Id, registry.Find(Id), null);

            registry.Register(Id, factory);

            Assert.False(cache.IsCurrent(Id, registry.Find(Id)));
            Assert.NotSame(first, cache.GetOrCreate(Id, registry.Find(Id), null));
        }

        [Fact]
        public void FactoryIsSharedButMutableStatesRemainPerMember()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var first = new BehaviourStateCache<object, object>();
            var second = new BehaviourStateCache<object, object>();
            registry.Register(Id, _ => new object());
            var registration = registry.Find(Id);

            Assert.NotSame(first.GetOrCreate(Id, registration, null), second.GetOrCreate(Id, registration, null));
            registry.Register("example.other", _ => new object());
            Assert.True(first.IsCurrent(Id, registry.Find(Id)));
            Assert.True(second.IsCurrent(Id, registry.Find(Id)));
        }

        [Fact]
        public void ThrowingReplacementCannotMasqueradeAsTheCachedState()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            registry.Register(Id, _ => new object());
            var original = registry.Find(Id);
            object first = cache.GetOrCreate(Id, original, null);
            registry.Register(Id, _ => throw new InvalidOperationException());
            var replacement = registry.Find(Id);

            Assert.Throws<InvalidOperationException>(() => cache.GetOrCreate(Id, replacement, null));

            Assert.False(cache.IsCurrent(Id, replacement));
            Assert.Same(first, cache.GetOrCreate(Id, original, null));
            Assert.True(registry.RemoveIfCurrent(Id, replacement));
        }

        [Fact]
        public void FailureFromOldFactoryCannotUnregisterItsReplacement()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            var successorState = new object();
            registry.Register(Id, _ =>
            {
                registry.Register(Id, __ => successorState);
                throw new InvalidOperationException();
            });
            var original = registry.Find(Id);

            Assert.Throws<InvalidOperationException>(() => cache.GetOrCreate(Id, original, null));

            Assert.False(registry.RemoveIfCurrent(Id, original));
            Assert.Same(successorState, cache.GetOrCreate(Id, registry.Find(Id), null));
        }

        [Fact]
        public void NullResultFromOldFactoryLeavesItsSuccessorAvailableAndUncached()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            var successorState = new object();
            registry.Register(Id, _ =>
            {
                registry.Register(Id, __ => successorState);
                return null;
            });
            var original = registry.Find(Id);

            Assert.Null(cache.GetOrCreate(Id, original, null));

            Assert.False(registry.RemoveIfCurrent(Id, original));
            Assert.NotNull(registry.Find(Id));
            Assert.False(cache.IsCurrent(Id, registry.Find(Id)));
            Assert.Same(successorState, cache.GetOrCreate(Id, registry.Find(Id), null));
        }

        [Fact]
        public void NullFactoryResultRemovesOnlyItsOwnRegistration()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            registry.Register(Id, _ => null);
            var registration = registry.Find(Id);

            Assert.Null(cache.GetOrCreate(Id, registration, null));
            Assert.True(registry.RemoveIfCurrent(Id, registration));
            cache.ObserveMissing(Id);

            Assert.Null(registry.Find(Id));
            Assert.True(cache.IsCurrent(Id, registry.Find(Id)));
        }

        [Fact]
        public void FailedInitialAttemptStillWakesSurfaceWhenFactoryInstallsSuccessor()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            var successorState = new object();
            registry.Register(Id, _ =>
            {
                registry.Register(Id, __ => successorState);
                throw new InvalidOperationException();
            });
            var failed = registry.Find(Id);
            Assert.Throws<InvalidOperationException>(() => cache.GetOrCreate(Id, failed, null));
            Assert.False(cache.Contains(Id));

            Assert.False(registry.RemoveIfCurrent(Id, failed));
            cache.ObserveMissing(Id);

            // The member's stale-controller guard first requires an observed entry.
            // Recording this unsuccessful first attempt makes the successor visible.
            Assert.True(cache.Contains(Id));
            Assert.False(cache.IsCurrent(Id, registry.Find(Id)));
            Assert.Same(successorState, cache.GetOrCreate(Id, registry.Find(Id), null));
        }

        [Fact]
        public void FirstLateRegistrationInvalidatesAnUnusedCache()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            Assert.True(cache.IsCurrent(Id, registry.Find(Id)));

            registry.Register(Id, _ => new object());

            Assert.False(cache.IsCurrent(Id, registry.Find(Id)));
        }

        [Fact]
        public void AcknowledgedRemovalStaysQuietUntilTheBehaviourIsRegisteredAgain()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            registry.Register(Id, _ => new object());
            cache.GetOrCreate(Id, registry.Find(Id), null);
            registry.Remove(Id);
            Assert.False(cache.IsCurrent(Id, registry.Find(Id)));

            cache.ObserveMissing(Id);

            Assert.True(cache.Contains(Id));
            Assert.True(cache.IsCurrent(Id, registry.Find(Id)));
            registry.Register(Id, _ => new object());
            Assert.False(cache.IsCurrent(Id, registry.Find(Id)));
            Assert.NotNull(cache.GetOrCreate(Id, registry.Find(Id), null));
        }

        [Fact]
        public void SurfaceMemberWithoutAnInitialFactoryCanNoticeItsFirstRegistration()
        {
            var registry = new BehaviourFactoryRegistry<object, object>();
            var cache = new BehaviourStateCache<object, object>();
            Assert.False(cache.Contains(Id));
            cache.ObserveMissing(Id);
            Assert.True(cache.Contains(Id));
            Assert.True(cache.IsCurrent(Id, registry.Find(Id)));

            registry.Register(Id, _ => new object());

            Assert.False(cache.IsCurrent(Id, registry.Find(Id)));
        }
    }
}
