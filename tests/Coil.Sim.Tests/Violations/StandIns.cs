// Stand-ins for the two assemblies this project must never reference.
//
// NetArchTest matches a dependency by namespace prefix, so a type in namespace
// Coil.Agents or Coil.Presentation is enough to trip the rules that forbid
// them — without Coil.Sim.Tests taking a real dependency on either (M0-09).
// The genuine Coil.Agents assembly is referenced separately and holds no types
// yet; these never collide with it.

namespace Coil.Agents
{
    public sealed class StandIn
    {
        public int Value { get; init; }
    }
}

namespace Coil.Presentation
{
    public sealed class StandIn
    {
        public int Value { get; init; }
    }
}
