using System.Linq;

static partial class C
{
    public static bool Compound(int[] ids, int[] parents) => ids.Any(id => IsLab(id) && parents.Any(parent => parent == id)); // CR0403: id =>
    public static bool Deep(int[] ids, int[] parents) => ids.Any(id => parents.All(parent => ids.Any(child => child == parent))); // CR0403: id =>
}
