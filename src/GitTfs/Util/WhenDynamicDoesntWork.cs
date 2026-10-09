
namespace GitTfs.Util
{
    using System.Reflection;
    public static class WhenDynamicDoesntWork
    {
        public static T Call<T>(this object o, string method, params object[] args) => (T)o.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, o, args);
    }
}