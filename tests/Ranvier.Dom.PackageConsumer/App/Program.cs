using System;

if (Bridge.NodeContract != typeof(Ranvier.Dom.Node))
    throw new Exception("Browser and native packages have different public contract identities");
if (Bridge.Identity((Ranvier.Dom.Node)null) != null)
    throw new Exception("A cross-package interface call changed the value");
Console.WriteLine("DOM_PACKAGE_IDENTITY PASS " + Bridge.NodeContract.Assembly.FullName);
