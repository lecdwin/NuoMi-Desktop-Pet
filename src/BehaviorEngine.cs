using System;

namespace NuoMiDesktopPet
{
    /// <summary>
    /// The logical actions understood by the pet.  The presentation layer is
    /// intentionally free to give every action more than one animation.
    /// </summary>
    internal enum CatBehavior
    {
        Idle,
        Observe,
        Pounce,
        CupPush,
        Begging,
        Eating,
        Purring,
        Grooming,
        Stretching,
        Sleeping,
        Zoomies,
        Playing
    }

    /// <summary>
    /// Keeps the pet's needs and chooses small autonomous actions.  Time values
    /// passed as long values use the caller's monotonic millisecond clock; UTC
    /// is used only for needs and persisted/offline progress.
    /// </summary>
    internal sealed class BehaviorEngine
    {
        private const double MaximumNeed = 100.0;
        private const double MaximumActiveCatchUpHours = 0.5;
        private const double MaximumOfflineCatchUpHours = 8.0;

        private readonly Random _random;
        private readonly CatBehavior[] _recentBehaviors =
            new CatBehavior[3];
        private readonly long[] _cooldownUntil =
            new long[12];
        private int _recentBehaviorCount;
        private bool _isRunning;
        private int _priority;
        private long _nextAutonomousAt;

        public BehaviorEngine(Random random)
        {
            _random = random ?? new Random();

            Hunger = 28.0;
            Energy = 82.0;
            Mood = 72.0;
            Affection = 50.0;
            Boredom = 20.0;

            Current = CatBehavior.Idle;
            StartedAt = 0L;
            Until = 0L;
            LastNeedsUtc = DateTime.UtcNow;
            _priority = Int32.MinValue;
        }

        public double Hunger { get; private set; }

        public double Energy { get; private set; }

        public double Mood { get; private set; }

        public double Affection { get; private set; }

        public double Boredom { get; private set; }

        public CatBehavior Current { get; private set; }

        public long StartedAt { get; private set; }

        public long Until { get; private set; }

        public DateTime LastNeedsUtc { get; private set; }

        public bool IsBusy
        {
            get { return _isRunning; }
        }

        public int Priority
        {
            get { return _priority; }
        }

        public long NextAutonomousAt
        {
            get { return _nextAutonomousAt; }
        }

        /// <summary>
        /// Restores persistent needs.  Momentary animations deliberately return
        /// to Idle because monotonic timestamps cannot be carried across runs.
        /// </summary>
        public void Restore(
            double hunger,
            double energy,
            double mood,
            double affection,
            double boredom,
            DateTime lastNeedsUtc)
        {
            Hunger = ClampNeed(hunger, 28.0);
            Energy = ClampNeed(energy, 82.0);
            Mood = ClampNeed(mood, 72.0);
            Affection = ClampNeed(affection, 50.0);
            Boredom = ClampNeed(boredom, 20.0);
            LastNeedsUtc = NormalizeUtc(lastNeedsUtc);

            Current = CatBehavior.Idle;
            StartedAt = 0L;
            Until = 0L;
            _isRunning = false;
            _priority = Int32.MinValue;
            _nextAutonomousAt = 0L;
            _recentBehaviorCount = 0;
            Array.Clear(_cooldownUntil, 0, _cooldownUntil.Length);
        }

        /// <summary>
        /// Advances slowly changing needs.  Foreground catch-up is capped at
        /// thirty minutes and offline catch-up at eight hours so clock changes
        /// or a long absence never punish the owner.
        /// </summary>
        public void AdvanceNeeds(DateTime utcNow, bool isActive)
        {
            DateTime normalizedNow = NormalizeUtc(utcNow);
            TimeSpan elapsed = normalizedNow - LastNeedsUtc;

            // Rebase even when the system clock moves backwards.
            LastNeedsUtc = normalizedNow;
            if (elapsed.Ticks <= 0L)
            {
                return;
            }

            double elapsedHours = elapsed.TotalHours;
            double limit = isActive
                ? MaximumActiveCatchUpHours
                : MaximumOfflineCatchUpHours;
            if (elapsedHours > limit)
            {
                elapsedHours = limit;
            }

            if (isActive)
            {
                Hunger += 3.8 * elapsedHours;

                if (_isRunning && Current == CatBehavior.Sleeping)
                {
                    Energy += 14.0 * elapsedHours;
                    Boredom += 0.7 * elapsedHours;
                }
                else
                {
                    Energy -= 2.6 * elapsedHours;
                    Boredom += 4.5 * elapsedHours;
                }

                Affection -= 0.08 * elapsedHours;
            }
            else
            {
                // While the program is away the cat rests and its other needs
                // progress at a gentler rate.
                Hunger += 1.7 * elapsedHours;
                Energy += 5.5 * elapsedHours;
                Boredom += 0.9 * elapsedHours;
                Affection -= 0.02 * elapsedHours;
            }

            double discomfort = 0.0;
            if (Hunger > 72.0)
            {
                discomfort += (Hunger - 72.0) / 28.0;
            }
            if (Boredom > 68.0)
            {
                discomfort += (Boredom - 68.0) / 32.0;
            }
            if (Energy < 25.0)
            {
                discomfort += (25.0 - Energy) / 25.0;
            }

            if (discomfort > 0.0)
            {
                Mood -= discomfort * (isActive ? 1.8 : 0.55) * elapsedHours;
            }
            else if (Mood < 68.0)
            {
                Mood += 0.35 * elapsedHours;
            }

            ClampAllNeeds();
        }

        /// <summary>
        /// Starts an action.  A running action may only be replaced by one with
        /// an equal or higher priority.
        /// </summary>
        public bool Start(CatBehavior behavior, long now, long duration, int priority)
        {
            if (duration < 1L)
            {
                duration = 1L;
            }

            if (_isRunning)
            {
                if (now >= Until)
                {
                    Complete(now);
                }
                else if (priority < _priority)
                {
                    return false;
                }
            }

            Current = behavior;
            StartedAt = now;
            Until = SafeAdd(now, duration);
            _priority = priority;
            _isRunning = true;
            RememberBehavior(behavior, now);
            return true;
        }

        /// <summary>
        /// Returns normalized action progress in the range zero through one.
        /// An engine with no running action is already complete.
        /// </summary>
        public double Progress(long now)
        {
            if (!_isRunning)
            {
                return 1.0;
            }

            if (now <= StartedAt)
            {
                return 0.0;
            }
            if (now >= Until || Until <= StartedAt)
            {
                return 1.0;
            }

            return (double)(now - StartedAt) / (double)(Until - StartedAt);
        }

        /// <summary>
        /// Completes the current action and applies its effect to the needs.
        /// </summary>
        public bool Complete(long now)
        {
            if (!_isRunning)
            {
                return false;
            }

            ApplyCompletion(Current);
            ResetAction(now);
            return true;
        }

        /// <summary>
        /// Stops an interrupted action without granting its completion effect.
        /// </summary>
        public bool Cancel(long now)
        {
            if (!_isRunning)
            {
                return false;
            }

            ResetAction(now);
            return true;
        }

        public bool CanAutoStart(long now)
        {
            return !_isRunning && now >= _nextAutonomousAt;
        }

        /// <summary>
        /// Chooses, but does not start, a context-aware autonomous action.
        /// Cursor speed is expected in device-independent pixels per second.
        /// </summary>
        public CatBehavior ChooseAutonomous(
            long now,
            double cursorSpeed,
            bool cursorNear)
        {
            if (!CanAutoStart(now))
            {
                return CatBehavior.Idle;
            }

            if (Double.IsNaN(cursorSpeed) ||
                Double.IsInfinity(cursorSpeed) ||
                cursorSpeed < 0.0)
            {
                cursorSpeed = 0.0;
            }

            double[] weights = new double[12];

            weights[(int)CatBehavior.Idle] = 8.0;
            weights[(int)CatBehavior.Observe] = 15.0;
            weights[(int)CatBehavior.Grooming] = 5.0 + Mood * 0.035;
            weights[(int)CatBehavior.Stretching] = 4.0 + Energy * 0.025;
            weights[(int)CatBehavior.Purring] =
                1.5 + Affection * 0.045 + Mood * 0.025;

            weights[(int)CatBehavior.Begging] =
                Hunger < 42.0 ? 0.0 : (Hunger - 42.0) * 0.34;
            if (Hunger >= 82.0)
            {
                weights[(int)CatBehavior.Begging] += 28.0;
            }

            weights[(int)CatBehavior.Sleeping] =
                Energy > 52.0 ? 0.5 : (52.0 - Energy) * 0.32;
            if (Energy <= 18.0)
            {
                weights[(int)CatBehavior.Sleeping] += 32.0;
            }

            double playfulEnergy = Math.Max(0.0, Energy - 22.0) / 78.0;
            double boredomDrive = Boredom / 100.0;
            weights[(int)CatBehavior.Playing] =
                (2.0 + boredomDrive * 20.0) * playfulEnergy;
            weights[(int)CatBehavior.CupPush] =
                Math.Max(0.0, Boredom - 38.0) * 0.20 * playfulEnergy;
            weights[(int)CatBehavior.Zoomies] =
                Math.Max(0.0, Boredom - 55.0) * 0.30 *
                Math.Max(0.0, Energy - 48.0) / 52.0;

            if (cursorNear)
            {
                weights[(int)CatBehavior.Observe] += 8.0;
                weights[(int)CatBehavior.Playing] += 7.0;

                if (cursorSpeed >= 90.0)
                {
                    double chaseDrive = Math.Min(cursorSpeed, 1400.0) / 1400.0;
                    weights[(int)CatBehavior.Pounce] =
                        (9.0 + 30.0 * chaseDrive) * playfulEnergy;
                }
            }

            // Eating is initiated by receiving food, not conjured by an
            // autonomous choice.
            weights[(int)CatBehavior.Eating] = 0.0;

            ApplyHistoryAndCooldown(weights, now);
            return PickWeighted(weights);
        }

        /// <summary>
        /// Chooses a small desk-safe action that can coexist with the keyboard
        /// and mouse pose.  It deliberately excludes actions that move the
        /// window, create props or take control of the paws.
        /// </summary>
        public CatBehavior ChooseDeskIdle(long now)
        {
            if (!CanAutoStart(now))
            {
                return CatBehavior.Idle;
            }

            double[] weights = new double[12];
            weights[(int)CatBehavior.Idle] = 12.0;
            weights[(int)CatBehavior.Observe] = 27.0;
            weights[(int)CatBehavior.Purring] =
                2.0 + Affection * 0.035 + Mood * 0.018;
            weights[(int)CatBehavior.Stretching] =
                Energy > 25.0
                    ? 6.0 + Energy * 0.015
                    : 1.0;
            weights[(int)CatBehavior.Sleeping] =
                Energy < 34.0
                    ? 4.0 + (34.0 - Energy) * 0.42
                    : 0.0;
            ApplyHistoryAndCooldown(weights, now);
            return PickWeighted(weights);
        }

        /// <summary>
        /// Schedules the next autonomous decision using an inclusive delay
        /// range measured in milliseconds.
        /// </summary>
        public void ScheduleNext(long now, int minimumDelay, int maximumDelay)
        {
            if (minimumDelay < 0)
            {
                minimumDelay = 0;
            }
            if (maximumDelay < minimumDelay)
            {
                maximumDelay = minimumDelay;
            }

            long span = (long)maximumDelay - (long)minimumDelay;
            long randomPart = (long)Math.Floor(_random.NextDouble() * (span + 1.0));
            _nextAutonomousAt = SafeAdd(now, (long)minimumDelay + randomPart);
        }

        private void ApplyCompletion(CatBehavior completed)
        {
            switch (completed)
            {
                case CatBehavior.Observe:
                    Energy -= 0.8;
                    Mood += 1.0;
                    Boredom -= 5.0;
                    break;

                case CatBehavior.Pounce:
                    Hunger += 3.5;
                    Energy -= 8.0;
                    Mood += 7.0;
                    Affection += 1.5;
                    Boredom -= 18.0;
                    break;

                case CatBehavior.CupPush:
                    Hunger += 2.0;
                    Energy -= 5.0;
                    Mood += 6.0;
                    Boredom -= 15.0;
                    break;

                case CatBehavior.Begging:
                    Energy -= 1.0;
                    Affection += 1.0;
                    Boredom -= 2.0;
                    break;

                case CatBehavior.Eating:
                    Hunger -= 34.0;
                    Energy += 5.0;
                    Mood += 7.0;
                    Affection += 2.0;
                    Boredom -= 3.0;
                    break;

                case CatBehavior.Purring:
                    Energy += 1.0;
                    Mood += 8.0;
                    Affection += 5.0;
                    Boredom -= 5.0;
                    break;

                case CatBehavior.Grooming:
                    Energy -= 2.5;
                    Mood += 4.0;
                    Boredom -= 4.0;
                    break;

                case CatBehavior.Stretching:
                    Energy += 4.0;
                    Mood += 2.0;
                    Boredom -= 2.0;
                    break;

                case CatBehavior.Sleeping:
                    Hunger += 8.0;
                    Energy += 38.0;
                    Mood += 4.0;
                    Boredom += 3.0;
                    break;

                case CatBehavior.Zoomies:
                    Hunger += 7.0;
                    Energy -= 23.0;
                    Mood += 12.0;
                    Boredom -= 32.0;
                    break;

                case CatBehavior.Playing:
                    Hunger += 5.0;
                    Energy -= 12.0;
                    Mood += 10.0;
                    Affection += 4.0;
                    Boredom -= 26.0;
                    break;

                case CatBehavior.Idle:
                default:
                    break;
            }

            ClampAllNeeds();
        }

        private void ResetAction(long now)
        {
            Current = CatBehavior.Idle;
            StartedAt = now;
            Until = now;
            _priority = Int32.MinValue;
            _isRunning = false;
        }

        private CatBehavior PickWeighted(double[] weights)
        {
            double total = 0.0;
            int index;
            for (index = 0; index < weights.Length; index++)
            {
                if (weights[index] > 0.0)
                {
                    total += weights[index];
                }
            }

            if (total <= 0.0)
            {
                return CatBehavior.Idle;
            }

            double choice = _random.NextDouble() * total;
            for (index = 0; index < weights.Length; index++)
            {
                double weight = weights[index];
                if (weight <= 0.0)
                {
                    continue;
                }

                if (choice < weight)
                {
                    return (CatBehavior)index;
                }
                choice -= weight;
            }

            return CatBehavior.Idle;
        }

        private void RememberBehavior(CatBehavior behavior, long now)
        {
            if (behavior == CatBehavior.Idle)
            {
                return;
            }

            for (int index = _recentBehaviors.Length - 1; index > 0; index--)
            {
                _recentBehaviors[index] = _recentBehaviors[index - 1];
            }
            _recentBehaviors[0] = behavior;
            if (_recentBehaviorCount < _recentBehaviors.Length)
            {
                _recentBehaviorCount++;
            }

            int minimum;
            int maximum;
            GetCooldownRange(behavior, out minimum, out maximum);
            int delay = minimum;
            if (maximum > minimum)
            {
                delay += _random.Next(maximum - minimum + 1);
            }
            _cooldownUntil[(int)behavior] = SafeAdd(now, delay);
        }

        private void ApplyHistoryAndCooldown(double[] weights, long now)
        {
            int count = Math.Min(weights.Length, _cooldownUntil.Length);
            for (int index = 0; index < count; index++)
            {
                if (index != (int)CatBehavior.Idle &&
                    now < _cooldownUntil[index])
                {
                    weights[index] = 0.0;
                }
            }

            double[] penalties = new double[] { 0.18, 0.45, 0.70 };
            for (int recentIndex = 0;
                recentIndex < _recentBehaviorCount;
                recentIndex++)
            {
                int behaviorIndex = (int)_recentBehaviors[recentIndex];
                if (behaviorIndex >= 0 &&
                    behaviorIndex < weights.Length)
                {
                    weights[behaviorIndex] *= penalties[recentIndex];
                }
            }
        }

        private static void GetCooldownRange(
            CatBehavior behavior,
            out int minimum,
            out int maximum)
        {
            switch (behavior)
            {
                case CatBehavior.Observe:
                    minimum = 8000;
                    maximum = 15000;
                    break;
                case CatBehavior.Pounce:
                    minimum = 20000;
                    maximum = 45000;
                    break;
                case CatBehavior.CupPush:
                    minimum = 90000;
                    maximum = 180000;
                    break;
                case CatBehavior.Begging:
                    minimum = 20000;
                    maximum = 45000;
                    break;
                case CatBehavior.Eating:
                    minimum = 30000;
                    maximum = 70000;
                    break;
                case CatBehavior.Purring:
                    minimum = 25000;
                    maximum = 60000;
                    break;
                case CatBehavior.Grooming:
                    minimum = 35000;
                    maximum = 80000;
                    break;
                case CatBehavior.Stretching:
                    minimum = 30000;
                    maximum = 65000;
                    break;
                case CatBehavior.Sleeping:
                    minimum = 120000;
                    maximum = 300000;
                    break;
                case CatBehavior.Zoomies:
                    minimum = 90000;
                    maximum = 180000;
                    break;
                case CatBehavior.Playing:
                    minimum = 35000;
                    maximum = 80000;
                    break;
                default:
                    minimum = 5000;
                    maximum = 12000;
                    break;
            }
        }

        private void ClampAllNeeds()
        {
            Hunger = ClampNeed(Hunger, 28.0);
            Energy = ClampNeed(Energy, 82.0);
            Mood = ClampNeed(Mood, 72.0);
            Affection = ClampNeed(Affection, 50.0);
            Boredom = ClampNeed(Boredom, 20.0);
        }

        private static double ClampNeed(double value, double fallback)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value))
            {
                return fallback;
            }
            if (value < 0.0)
            {
                return 0.0;
            }
            if (value > MaximumNeed)
            {
                return MaximumNeed;
            }
            return value;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }
            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static long SafeAdd(long value, long amount)
        {
            if (amount > 0L && value > Int64.MaxValue - amount)
            {
                return Int64.MaxValue;
            }
            if (amount < 0L && value < Int64.MinValue - amount)
            {
                return Int64.MinValue;
            }
            return value + amount;
        }
    }
}
