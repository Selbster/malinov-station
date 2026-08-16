namespace Content.Server.NPC.Pathfinding;

[Flags]
public enum PathFlags : byte
{
    None = 0,

    /// <summary>
    /// Do we have any form of access.
    /// </summary>
    Access = 1 << 0,

    /// <summary>
    /// Can we pry airlocks if necessary.
    /// </summary>
    Prying = 1 << 1,

    /// <summary>
    /// Can stuff like walls be broken.
    /// </summary>
    Smashing = 1 << 2,

    /// <summary>
    /// Can we climb it like a table or railing.
    /// </summary>
    Climbing = 1 << 3,

    /// <summary>
    /// Can we open stuff that requires interaction (e.g. click-open doors).
    /// </summary>
    Interact = 1 << 4,

    /// <summary>
    /// Can we use our own held access (ID/PDA) to open doors that require it - a real per-door
    /// <see cref="Content.Shared.Access.Systems.AccessReaderSystem.IsAllowed"/> check happens at steering
    /// time (see NPCSteeringSystem.Obstacles.cs), same as a player's own click would. Unlike
    /// <see cref="Prying"/>, this never forces a door open; if we're not actually authorised for it, we
    /// just don't open it. Opt-in and separate from <see cref="Interact"/> so existing NPCs (dragon, xenos,
    /// monkeys) that already set NavInteract keep their current behaviour around access-locked doors.
    /// </summary>
    AccessInteract = 1 << 5,

    /// <summary>
    /// Don't treat doors as collision-avoidance obstacles to steer away from (see
    /// NPCSteeringSystem.Context.cs's CollisionAvoidance, which otherwise pushes an NPC away from any
    /// nearby solid door - including the one it's deliberately walking up to open - causing it to
    /// oscillate between approaching and backing off). Opt-in and separate from <see cref="Interact"/> so
    /// existing door-using NPCs keep their current (if slightly jittery) approach behaviour.
    /// </summary>
    GentleApproach = 1 << 6,
}
