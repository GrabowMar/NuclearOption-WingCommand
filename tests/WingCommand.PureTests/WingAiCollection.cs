using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// The reflex registry is static, because in the game there is exactly one wing and one
    /// set of reflexes shared by every member. xunit runs test classes in parallel by
    /// default, so two classes clearing and repopulating that registry at the same time
    /// produce failures that have nothing to do with the code under test. Everything that
    /// touches <see cref="WingAi"/> shares this collection and therefore runs serially.
    /// </summary>
    [CollectionDefinition("WingAi", DisableParallelization = true)]
    public sealed class WingAiCollection { }
}
