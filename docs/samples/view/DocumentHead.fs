/// The F# samples of docs/content/view/document-head.md, in page order; a sample that is an excerpt is wrapped in a function.
module DocumentHead

open Fable.Core.TS
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

type Article = { Title: Signal<string>; Summary: string; Slug: string }

let articlePage =
    Component.define (fun _ (article: Article) -> article) (fun t ->
        article () {
            t.Document.title (fun a -> a.Title.Value + " · Journal")
            t.Document.meta [| t.Attrs.attribute ("name", "description"); t.Attrs.attribute ("content", fun a -> Some a.Summary) |]
            t.Document.link [| t.Attrs.attribute ("rel", "canonical"); t.Attrs.attribute ("href", fun a -> Some ("/articles/" + a.Slug)) |]
            h1 () { fun a -> a.Title.Value }
        })

let options = { Server.Options.defaults with Policy = Omit }

let renderArticle () =
    let page = Server.renderPage options articlePage { Title = createSignal "Tides"; Summary = "On \"moons\""; Slug = "tides" }
    // page.Body: <!--[r--><article><h1>Tides</h1></article><!--]-->
    // page.Head:
    page
