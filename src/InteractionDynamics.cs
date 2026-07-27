using System;

namespace NuoMiDesktopPet
{
    internal enum MotionPersonality
    {
        Quiet = 0,
        Natural = 1,
        Playful = 2
    }

    /// <summary>
    /// Turns instantaneous keyboard and mouse events into small, overlapping
    /// motions.  The visible pose can therefore have anticipation, contact,
    /// recoil and a short settle instead of snapping between two angles.
    /// </summary>
    internal sealed class InteractionDynamics
    {
        private readonly Random _random;
        private readonly double _idlePhaseA;
        private readonly double _idlePhaseB;

        private MotionPersonality _personality;
        private long _lastKeyStrikeAt = -10000L;
        private long _lastKeyReleaseAt = -10000L;
        private double _keyReleaseStart;
        private long _lastLeftMouseStrikeAt = -10000L;
        private long _lastLeftMouseReleaseAt = -10000L;
        private double _leftMouseReleaseStart;
        private long _lastRightMouseStrikeAt = -10000L;
        private long _lastRightMouseReleaseAt = -10000L;
        private double _rightMouseReleaseStart;
        private long _lastWheelAt = -10000L;
        private long _lastInputAt = -10000L;
        private long _lastDistinctKeyAt = -10000L;
        private int _lastVirtualKey = -1;
        private int _wheelDirection;

        private double _keyReachTarget;
        private double _keyReach;
        private double _keyReachVelocity;
        private double _keyRowTarget;
        private double _keyRow;
        private double _keyRowVelocity;
        private double _bodyReaction;
        private double _bodyReactionVelocity;
        private double _headReaction;
        private double _headReactionVelocity;
        private double _tailKick;
        private double _tailKickVelocity;
        private double _typingEnergy;
        private double _mouseEnergy;
        private double _engagement;

        public InteractionDynamics(Random random)
        {
            _random = random ?? new Random();
            _idlePhaseA = _random.NextDouble() * Math.PI * 2.0;
            _idlePhaseB = _random.NextDouble() * Math.PI * 2.0;
            Personality = MotionPersonality.Natural;
        }

        public MotionPersonality Personality
        {
            get { return _personality; }
            set
            {
                if (value < MotionPersonality.Quiet ||
                    value > MotionPersonality.Playful)
                {
                    value = MotionPersonality.Natural;
                }
                _personality = value;
            }
        }

        public double PersonalityScale
        {
            get
            {
                switch (_personality)
                {
                    case MotionPersonality.Quiet:
                        return 0.72;
                    case MotionPersonality.Playful:
                        return 1.28;
                    default:
                        return 1.0;
                }
            }
        }

        public double KeyboardContact { get; private set; }

        public double KeyboardRecoil { get; private set; }

        public double KeyboardReach
        {
            get { return _keyReach; }
        }

        public double KeyboardRow
        {
            get { return _keyRow; }
        }

        public double LeftMouseContact { get; private set; }

        public double LeftMouseRecoil { get; private set; }

        public double RightMouseContact { get; private set; }

        public double RightMouseRecoil { get; private set; }

        public double WheelMotion { get; private set; }

        public double BodyReaction
        {
            get { return _bodyReaction; }
        }

        public double HeadReaction
        {
            get { return _headReaction; }
        }

        public double TailKick
        {
            get { return _tailKick; }
        }

        public double TypingEnergy
        {
            get { return _typingEnergy; }
        }

        public double MouseEnergy
        {
            get { return _mouseEnergy; }
        }

        public double Engagement
        {
            get { return _engagement; }
        }

        public long LastInputAt
        {
            get { return _lastInputAt; }
        }

        public void RegisterKeyDown(
            int virtualKey,
            double normalizedReach,
            double normalizedRow,
            bool isRepeat,
            long now)
        {
            double scale = PersonalityScale;
            long spacing = now - _lastKeyStrikeAt;
            bool rapid = spacing >= 0L && spacing < 155L;
            bool alternating =
                virtualKey != _lastVirtualKey &&
                now - _lastDistinctKeyAt < 280L;

            _lastKeyStrikeAt = now;
            _lastInputAt = now;
            _keyReachTarget = Clamp(normalizedReach, -1.0, 1.0);
            _keyRowTarget = Clamp(normalizedRow, -1.0, 1.0);
            _typingEnergy = Clamp(
                _typingEnergy +
                (isRepeat ? 0.075 : (rapid ? 0.17 : 0.25)) * scale,
                0.0,
                1.0);

            // A fresh tap compresses the chest first, then the damped spring
            // creates a tiny overshoot on the way back up.
            _bodyReactionVelocity +=
                (isRepeat ? 0.30 : (rapid ? 0.48 : 0.72)) * scale;
            _headReactionVelocity +=
                (alternating ? 0.42 : 0.31) * scale;
            _tailKickVelocity +=
                (rapid ? 1.08 : 0.72) * scale *
                (_keyReachTarget >= 0.0 ? 1.0 : -1.0);

            if (virtualKey != _lastVirtualKey)
            {
                _lastDistinctKeyAt = now;
                _lastVirtualKey = virtualKey;
            }
        }

        public void RegisterMouseDown(bool physicalLeft, long now)
        {
            double scale = PersonalityScale;
            if (physicalLeft)
            {
                _lastLeftMouseStrikeAt = now;
                _tailKickVelocity += 0.58 * scale;
            }
            else
            {
                _lastRightMouseStrikeAt = now;
                _tailKickVelocity -= 0.58 * scale;
            }

            _lastInputAt = now;
            _mouseEnergy = Clamp(_mouseEnergy + 0.28 * scale, 0.0, 1.0);
            _bodyReactionVelocity += 0.48 * scale;
            _headReactionVelocity += 0.22 * scale;
        }

        public void RegisterKeyUp(long now)
        {
            _lastKeyReleaseAt = now;
            _keyReleaseStart = Math.Max(
                KeyboardContact,
                StrikeContact(
                    now - _lastKeyStrikeAt,
                    true,
                    18.0,
                    55.0,
                    96.0,
                    205.0));
            _lastInputAt = now;
        }

        public void RetargetKeyboard(
            double normalizedReach,
            double normalizedRow)
        {
            _keyReachTarget = Clamp(normalizedReach, -1.0, 1.0);
            _keyRowTarget = Clamp(normalizedRow, -1.0, 1.0);
        }

        public void RegisterMouseUp(bool physicalLeft, long now)
        {
            if (physicalLeft)
            {
                _lastLeftMouseReleaseAt = now;
                _leftMouseReleaseStart = Math.Max(
                    LeftMouseContact,
                    StrikeContact(
                        now - _lastLeftMouseStrikeAt,
                        true,
                        12.0,
                        45.0,
                        92.0,
                        185.0));
            }
            else
            {
                _lastRightMouseReleaseAt = now;
                _rightMouseReleaseStart = Math.Max(
                    RightMouseContact,
                    StrikeContact(
                        now - _lastRightMouseStrikeAt,
                        true,
                        12.0,
                        45.0,
                        92.0,
                        185.0));
            }
            _lastInputAt = now;
        }

        public void RegisterWheel(int delta, long now)
        {
            if (delta == 0)
            {
                return;
            }

            double scale = PersonalityScale;
            _wheelDirection = delta > 0 ? 1 : -1;
            _lastWheelAt = now;
            _lastInputAt = now;
            _mouseEnergy = Clamp(_mouseEnergy + 0.18 * scale, 0.0, 1.0);
            _headReactionVelocity += 0.16 * scale * _wheelDirection;
            _tailKickVelocity -= 0.34 * scale * _wheelDirection;
        }

        public void RegisterTailTouch(long now)
        {
            double scale = PersonalityScale;
            double direction = _random.Next(2) == 0 ? -1.0 : 1.0;
            _lastInputAt = now;
            _tailKickVelocity += direction * 2.15 * scale;
            _bodyReactionVelocity += 0.34 * scale;
            _headReactionVelocity -= direction * 0.82 * scale;
            _engagement = Clamp(_engagement + 0.16, 0.0, 1.0);
        }

        public void RegisterDeskTap(long now)
        {
            double scale = PersonalityScale;
            _lastInputAt = now;
            _bodyReactionVelocity += 0.31 * scale;
            _headReactionVelocity +=
                (_random.NextDouble() - 0.5) *
                0.36 *
                scale;
            _tailKickVelocity +=
                (_random.NextDouble() - 0.5) *
                0.48 *
                scale;
            _engagement = Clamp(_engagement + 0.06, 0.0, 1.0);
        }

        public void Update(
            long now,
            double deltaSeconds,
            bool keyboardHeld,
            bool physicalLeftHeld,
            bool physicalRightHeld)
        {
            double remainingTime = Clamp(deltaSeconds, 0.0, 0.05);
            double scale = PersonalityScale;

            KeyboardContact = StrikeContact(
                now - _lastKeyStrikeAt,
                keyboardHeld,
                18.0,
                55.0,
                96.0,
                205.0);
            KeyboardContact = Math.Max(
                KeyboardContact,
                ReleaseContact(
                    now - _lastKeyReleaseAt,
                    _lastKeyReleaseAt >= _lastKeyStrikeAt
                        ? _keyReleaseStart
                        : 0.0,
                    165.0));
            KeyboardRecoil = StrikeRecoil(
                now - _lastKeyStrikeAt,
                88.0,
                225.0);

            LeftMouseContact = StrikeContact(
                now - _lastLeftMouseStrikeAt,
                physicalLeftHeld,
                12.0,
                45.0,
                92.0,
                185.0);
            LeftMouseContact = Math.Max(
                LeftMouseContact,
                ReleaseContact(
                    now - _lastLeftMouseReleaseAt,
                    _lastLeftMouseReleaseAt >= _lastLeftMouseStrikeAt
                        ? _leftMouseReleaseStart
                        : 0.0,
                    145.0));
            LeftMouseRecoil = StrikeRecoil(
                now - _lastLeftMouseStrikeAt,
                80.0,
                205.0);
            RightMouseContact = StrikeContact(
                now - _lastRightMouseStrikeAt,
                physicalRightHeld,
                12.0,
                45.0,
                92.0,
                185.0);
            RightMouseContact = Math.Max(
                RightMouseContact,
                ReleaseContact(
                    now - _lastRightMouseReleaseAt,
                    _lastRightMouseReleaseAt >= _lastRightMouseStrikeAt
                        ? _rightMouseReleaseStart
                        : 0.0,
                    145.0));
            RightMouseRecoil = StrikeRecoil(
                now - _lastRightMouseStrikeAt,
                80.0,
                205.0);

            long wheelAge = now - _lastWheelAt;
            if (wheelAge >= 0L && wheelAge < 260L)
            {
                double phase = wheelAge / 260.0;
                WheelMotion =
                    _wheelDirection *
                    Math.Sin(phase * Math.PI * 2.25) *
                    Math.Pow(1.0 - phase, 1.45);
            }
            else
            {
                WheelMotion = 0.0;
            }

            while (remainingTime > 0.0)
            {
                double dt = Math.Min(1.0 / 120.0, remainingTime);
                StepSpring(
                    ref _keyReach,
                    ref _keyReachVelocity,
                    _keyReachTarget,
                    1500.0,
                    58.0,
                    dt);
                StepSpring(
                    ref _keyRow,
                    ref _keyRowVelocity,
                    _keyRowTarget,
                    1050.0,
                    49.0,
                    dt);
                StepSpring(
                    ref _bodyReaction,
                    ref _bodyReactionVelocity,
                    0.0,
                    88.0,
                    16.0,
                    dt);
                StepSpring(
                    ref _headReaction,
                    ref _headReactionVelocity,
                    0.0,
                    72.0,
                    15.0,
                    dt);
                StepSpring(
                    ref _tailKick,
                    ref _tailKickVelocity,
                    0.0,
                    33.0,
                    8.8,
                    dt);

                _typingEnergy *= Math.Exp(-dt * 1.55);
                _mouseEnergy *= Math.Exp(-dt * 2.05);
                double engagementTarget = Clamp(
                    _typingEnergy * 0.88 +
                    _mouseEnergy * 0.72,
                    0.0,
                    1.0);
                double engagementRate = engagementTarget > _engagement
                    ? 9.5
                    : 2.4;
                _engagement +=
                    (engagementTarget - _engagement) *
                    (1.0 - Math.Exp(-engagementRate * dt));
                remainingTime -= dt;
            }

            _bodyReaction = Clamp(_bodyReaction, -0.16, 0.18 * scale);
            _bodyReactionVelocity = Clamp(
                _bodyReactionVelocity,
                -2.2 * scale,
                2.6 * scale);
            _headReaction = Clamp(_headReaction, -0.12, 0.14 * scale);
            _headReactionVelocity = Clamp(
                _headReactionVelocity,
                -1.8 * scale,
                2.0 * scale);
            _tailKick = Clamp(_tailKick, -0.16 * scale, 0.16 * scale);
            _tailKickVelocity = Clamp(
                _tailKickVelocity,
                -2.4 * scale,
                2.4 * scale);
        }

        public double GetIdleWeightShift(double seconds)
        {
            double slow =
                Math.Sin(seconds * 0.43 + _idlePhaseA) * 0.62 +
                Math.Sin(seconds * 0.19 + _idlePhaseB) * 0.38;
            return slow * PersonalityScale;
        }

        public double GetIdleHeadMicroTilt(double seconds)
        {
            double motion =
                Math.Sin(seconds * 0.71 + _idlePhaseB) * 0.58 +
                Math.Sin(seconds * 1.13 + _idlePhaseA) * 0.22;
            return motion * PersonalityScale;
        }

        public double GetIdleTailActivity(double seconds)
        {
            double wave =
                0.5 +
                0.5 *
                Math.Sin(
                    seconds * 0.21 +
                    _idlePhaseA +
                    Math.Sin(seconds * 0.073 + _idlePhaseB) * 0.72);
            return 0.16 + Math.Pow(wave, 3.0) * 0.84;
        }

        public void Clear()
        {
            _lastKeyStrikeAt = -10000L;
            _lastKeyReleaseAt = -10000L;
            _keyReleaseStart = 0.0;
            _lastLeftMouseStrikeAt = -10000L;
            _lastLeftMouseReleaseAt = -10000L;
            _leftMouseReleaseStart = 0.0;
            _lastRightMouseStrikeAt = -10000L;
            _lastRightMouseReleaseAt = -10000L;
            _rightMouseReleaseStart = 0.0;
            _lastWheelAt = -10000L;
            _lastInputAt = -10000L;
            _lastDistinctKeyAt = -10000L;
            _lastVirtualKey = -1;
            _wheelDirection = 0;
            _keyReachTarget = 0.0;
            _keyReach = 0.0;
            _keyReachVelocity = 0.0;
            _keyRowTarget = 0.0;
            _keyRow = 0.0;
            _keyRowVelocity = 0.0;
            _bodyReaction = 0.0;
            _bodyReactionVelocity = 0.0;
            _headReaction = 0.0;
            _headReactionVelocity = 0.0;
            _tailKick = 0.0;
            _tailKickVelocity = 0.0;
            _typingEnergy = 0.0;
            _mouseEnergy = 0.0;
            _engagement = 0.0;
            KeyboardContact = 0.0;
            KeyboardRecoil = 0.0;
            LeftMouseContact = 0.0;
            LeftMouseRecoil = 0.0;
            RightMouseContact = 0.0;
            RightMouseRecoil = 0.0;
            WheelMotion = 0.0;
        }

        private static double StrikeContact(
            long ageMilliseconds,
            bool held,
            double anticipationEnd,
            double contactAt,
            double settleStart,
            double settleEnd)
        {
            if (ageMilliseconds < 0L)
            {
                return held ? 0.82 : 0.0;
            }

            double age = ageMilliseconds;
            if (age < anticipationEnd)
            {
                return 0.0;
            }
            if (age < contactAt)
            {
                return EaseOutCubic(
                    (age - anticipationEnd) /
                    Math.Max(1.0, contactAt - anticipationEnd));
            }
            if (age < settleStart)
            {
                double compression =
                    Math.Sin(
                        (age - contactAt) /
                        Math.Max(1.0, settleStart - contactAt) *
                        Math.PI);
                return 1.0 + compression * 0.06;
            }
            if (held)
            {
                double settle = Clamp(
                    (age - settleStart) /
                    Math.Max(1.0, settleEnd - settleStart),
                    0.0,
                    1.0);
                return 1.0 - EaseInOut(settle) * 0.16;
            }
            if (age < settleEnd)
            {
                double release = Clamp(
                    (age - settleStart) /
                    Math.Max(1.0, settleEnd - settleStart),
                    0.0,
                    1.0);
                return 1.0 - EaseOutBack(release);
            }
            return held ? 0.84 : 0.0;
        }

        private static double StrikeRecoil(
            long ageMilliseconds,
            double start,
            double end)
        {
            if (ageMilliseconds < start ||
                ageMilliseconds >= end)
            {
                return 0.0;
            }

            double phase =
                (ageMilliseconds - start) /
                Math.Max(1.0, end - start);
            return
                Math.Sin(phase * Math.PI * 2.0) *
                Math.Pow(1.0 - phase, 1.25);
        }

        private static double ReleaseContact(
            long ageMilliseconds,
            double startAmount,
            double durationMilliseconds)
        {
            if (startAmount <= 0.0 ||
                ageMilliseconds < 0L ||
                ageMilliseconds >= durationMilliseconds)
            {
                return 0.0;
            }

            double progress =
                ageMilliseconds /
                Math.Max(1.0, durationMilliseconds);
            if (progress < 0.12)
            {
                return startAmount;
            }

            double release =
                (progress - 0.12) / 0.88;
            return startAmount * (1.0 - EaseInOut(release));
        }

        private static void StepSpring(
            ref double value,
            ref double velocity,
            double target,
            double stiffness,
            double damping,
            double deltaSeconds)
        {
            double acceleration =
                (target - value) * stiffness -
                velocity * damping;
            velocity += acceleration * deltaSeconds;
            value += velocity * deltaSeconds;
        }

        private static double EaseOutCubic(double value)
        {
            value = Clamp(value, 0.0, 1.0);
            double inverse = 1.0 - value;
            return 1.0 - inverse * inverse * inverse;
        }

        private static double EaseInOut(double value)
        {
            value = Clamp(value, 0.0, 1.0);
            return value * value * (3.0 - 2.0 * value);
        }

        private static double EaseOutBack(double value)
        {
            value = Clamp(value, 0.0, 1.0);
            const double overshoot = 1.70158;
            double shifted = value - 1.0;
            return 1.0 +
                (overshoot + 1.0) * shifted * shifted * shifted +
                overshoot * shifted * shifted;
        }

        private static double Clamp(
            double value,
            double minimum,
            double maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }
            if (value > maximum)
            {
                return maximum;
            }
            return value;
        }
    }
}
