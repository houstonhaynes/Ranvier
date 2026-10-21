namespace Ranvier.View.Sugar

open System
open Fable.Core.TS
open Ranvier.View.Representation
open Ranvier.View.Template

/// <summary>
/// SVG tags, each an SVG element under both commit modes. <c>text'</c>, <c>use'</c> and <c>title'</c> take an apostrophe:
/// <c>text</c> is the sugar's text slot, <c>use</c> an F# keyword and <c>title</c> the HTML tag.
/// </summary>
/// <remarks>
/// The first SVG tag used enables SVG creation: from then on the expanded path creates an element with an SVG name, and
/// every element under an SVG element other than a <c>foreignObject</c>, in the SVG namespace, and a row or arm template
/// rooted at SVG elements is parsed inside an svg element. Without an SVG tag, <c>t.element (HtmlTag "svg")</c> enables
/// nothing.
/// </remarks>
[<AutoOpen>]
module SvgElements =
#if FABLE_COMPILER
    [<Fable.Core.Emit("new Set($0.split(' '))")>]
    let private nameSet (names: string) : obj =
        Fable.Core.Util.jsNative

    do
        Ranvier.View.Representation.svgNames.Names <-
            nameSet
                "altGlyph altGlyphDef altGlyphItem animateColor animateMotion animateTransform clipPath feBlend feColorMatrix feComponentTransfer feComposite feConvolveMatrix feDiffuseLighting feDisplacementMap feDistantLight feDropShadow feFlood feFuncA feFuncB feFuncG feFuncR feGaussianBlur feImage feMerge feMergeNode feMorphology feOffset fePointLight feSpecularLighting feSpotLight feTile feTurbulence foreignObject glyphRef linearGradient radialGradient textPath attributeName attributeType baseFrequency baseProfile calcMode clipPathUnits diffuseConstant edgeMode filterUnits gradientTransform gradientUnits kernelMatrix kernelUnitLength keyPoints keySplines keyTimes lengthAdjust limitingConeAngle markerHeight markerUnits markerWidth maskContentUnits maskUnits numOctaves pathLength patternContentUnits patternTransform patternUnits pointsAtX pointsAtY pointsAtZ preserveAlpha preserveAspectRatio primitiveUnits refX refY repeatCount repeatDur requiredExtensions requiredFeatures specularConstant specularExponent spreadMethod startOffset stdDeviation stitchTiles surfaceScale systemLanguage tableValues targetX targetY textLength viewBox viewTarget xChannelSelector yChannelSelector zoomAndPan"

#endif
    type svg<'i> private (ps: Part<Dom.SVGSVGElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGSVGElement, 'i>((HtmlTag "svg"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGSVGElement, 'i> array) = svg<'i>(ps, ())

    type g<'i> private (ps: Part<Dom.SVGGElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGGElement, 'i>((HtmlTag "g"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGGElement, 'i> array) = g<'i>(ps, ())

    type path<'i> private (ps: Part<Dom.SVGPathElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGPathElement, 'i>((HtmlTag "path"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGPathElement, 'i> array) = path<'i>(ps, ())

    type circle<'i> private (ps: Part<Dom.SVGCircleElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGCircleElement, 'i>((HtmlTag "circle"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGCircleElement, 'i> array) = circle<'i>(ps, ())

    type ellipse<'i> private (ps: Part<Dom.SVGEllipseElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGEllipseElement, 'i>((HtmlTag "ellipse"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGEllipseElement, 'i> array) = ellipse<'i>(ps, ())

    type line<'i> private (ps: Part<Dom.SVGLineElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGLineElement, 'i>((HtmlTag "line"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGLineElement, 'i> array) = line<'i>(ps, ())

    type polyline<'i> private (ps: Part<Dom.SVGPolylineElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGPolylineElement, 'i>((HtmlTag "polyline"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGPolylineElement, 'i> array) = polyline<'i>(ps, ())

    type polygon<'i> private (ps: Part<Dom.SVGPolygonElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGPolygonElement, 'i>((HtmlTag "polygon"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGPolygonElement, 'i> array) = polygon<'i>(ps, ())

    type rect<'i> private (ps: Part<Dom.SVGRectElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGRectElement, 'i>((HtmlTag "rect"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGRectElement, 'i> array) = rect<'i>(ps, ())

    type text'<'i> private (ps: Part<Dom.SVGTextElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGTextElement, 'i>((HtmlTag "text"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGTextElement, 'i> array) = text'<'i>(ps, ())

    type tspan<'i> private (ps: Part<Dom.SVGTSpanElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGTSpanElement, 'i>((HtmlTag "tspan"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGTSpanElement, 'i> array) = tspan<'i>(ps, ())

    type use'<'i> private (ps: Part<Dom.SVGUseElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGUseElement, 'i>((HtmlTag "use"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGUseElement, 'i> array) = use'<'i>(ps, ())

    type defs<'i> private (ps: Part<Dom.SVGDefsElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGDefsElement, 'i>((HtmlTag "defs"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGDefsElement, 'i> array) = defs<'i>(ps, ())

    type linearGradient<'i> private (ps: Part<Dom.SVGLinearGradientElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGLinearGradientElement, 'i>((HtmlTag "linearGradient"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGLinearGradientElement, 'i> array) = linearGradient<'i>(ps, ())

    type radialGradient<'i> private (ps: Part<Dom.SVGRadialGradientElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGRadialGradientElement, 'i>((HtmlTag "radialGradient"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGRadialGradientElement, 'i> array) = radialGradient<'i>(ps, ())

    type stop<'i> private (ps: Part<Dom.SVGStopElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGStopElement, 'i>((HtmlTag "stop"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGStopElement, 'i> array) = stop<'i>(ps, ())

    type clipPath<'i> private (ps: Part<Dom.SVGClipPathElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGClipPathElement, 'i>((HtmlTag "clipPath"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGClipPathElement, 'i> array) = clipPath<'i>(ps, ())

    type mask<'i> private (ps: Part<Dom.SVGMaskElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGMaskElement, 'i>((HtmlTag "mask"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGMaskElement, 'i> array) = mask<'i>(ps, ())

    type foreignObject<'i> private (ps: Part<Dom.SVGForeignObjectElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGForeignObjectElement, 'i>((HtmlTag "foreignObject"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGForeignObjectElement, 'i> array) = foreignObject<'i>(ps, ())

    type title'<'i> private (ps: Part<Dom.SVGTitleElement, 'i> array, primary: unit) =
        inherit Element<Dom.SVGTitleElement, 'i>((HtmlTag "title"), ps)
        do Ranvier.View.Departures.enableSvg ()
        new([<ParamArray>] ps: Part<Dom.SVGTitleElement, 'i> array) = title'<'i>(ps, ())
