using System;

if (Bridge.NodeContract != typeof(Ranvier.Dom.Node))
    throw new Exception("Browser and native packages have different public contract identities");
if (Bridge.Identity((Ranvier.Dom.Node)null) != null)
    throw new Exception("A cross-package interface call changed the value");
if (!typeof(Ranvier.Dom.CSSStyleDeclaration).IsAssignableFrom(typeof(Ranvier.Dom.CSSStyleProperties)))
    throw new Exception("Inline style properties lost the CSS declaration contract");
if (typeof(Ranvier.Dom.ElementCSSInlineStyle).GetProperty("style")?.PropertyType != typeof(Ranvier.Dom.CSSStyleProperties))
    throw new Exception("The inline style getter is missing from the public mixin");
if (typeof(Ranvier.Dom.Attr).GetProperty("style")?.PropertyType != typeof(Ranvier.Dom.CSSStyleProperties))
    throw new Exception("The attribute style getter is missing from the public contract");
Console.WriteLine("DOM_PACKAGE_IDENTITY PASS " + Bridge.NodeContract.Assembly.FullName);
