public static class Bridge
{
    public static System.Type NodeContract => typeof(Ranvier.Dom.Node);
    public static Ranvier.Dom.Node Identity(Ranvier.Dom.Node value) => value;
}
