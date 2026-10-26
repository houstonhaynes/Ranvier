---
title: Native deployment with WebCore
description: Work in progress on native Ranvier views without JavaScript.
---

:::info In development
Native deployment through WebCore is being developed. This page describes the direction, not a
released deployment target or an installation procedure.
:::

The goal is to run Ranvier application code natively and use WebCore for the DOM, skipping
JavaScript entirely. You keep familiar view patterns: elements, attributes, events and bindings
that update the parts of the document that read reactive state.

![A native Ranvier application updates a DOM through WebCore, without a JavaScript compilation step.](/Ranvier/diagrams/native-webcore.svg)

For example, a counter still has a button, a click handler and a text binding. A click changes a
signal; the binding updates the button's text. The deployment work changes how those operations
reach the DOM, while keeping that authoring pattern.

Today's [browser target](getting-started.md) compiles F# to JavaScript with Fable.
The WebCore work explores a native route for the same style of views. It does not yet establish
package availability, supported operating systems or complete browser API compatibility.

Use [Ranvier.View.Dom](../guide/installation.md#views) for the documented browser path today.
The [roadmap](../concepts/roadmap.md) tracks the broader work.
