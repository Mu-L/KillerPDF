# Changelog

All notable changes to KillerPDF are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.8.0-alpha.1] - Unreleased

KillerPDF 1.8 begins the replacement of its legacy PdfSharpCore document pipeline with an independently authored .NET 10 PDF document engine. It is responsible for reading, validating, authoring, structurally editing, and writing PDF files. PDFium remains KillerPDF's rendering and display backend, while PdfPig continues to handle text extraction during the migration. This alpha is an engineering build, not a public release.

### PDF document engine development

- The primary document builder, content-stream builder, incremental page editor, and incremental annotation editor now have complete XML summaries across their public entry points, covering construction, conformance, pages, navigation, graphics state, paths, color, text, attachments, forms, annotations, editing, and serialization.
- Typed viewer preferences, tagged-PDF structure roles, axial and radial shadings, link appearances, page transitions, annotation metadata and flags, and blend modes now provide complete XML summaries for their public values and properties.
- Reusable tiling patterns and matrices, extended graphics states, alpha and luminosity soft masks, backdrop colors, and transparency-group Form XObjects now provide complete XML summaries across their public construction and state.
- OpenType font loading and metrics, embedding permissions, Unicode glyph mapping, calibrated Gray and RGB color spaces, Lab color spaces, and device-color gradient stops now provide complete XML summaries.
- The desktop regression-test project is now part of `KillerPDF.sln`, so a normal solution-wide test run covers all 1,404 engine tests and all 90 existing application tests instead of silently omitting the application suite.
- Incremental page editing can now set existing named checkbox and radio-button values without requesting viewer-generated appearances. Hierarchical field names, separate widget children, exact on and off appearance states, cleared radio selections, page-tree rebuilds, field-type validation, and button-kind rejection are handled while preserving the original byte prefix.
- Existing named text fields can now receive new plain-text values with regenerated normal appearances imported from the authoring engine. Field flags, maximum lengths, multiline, password, comb, alignment, font size, RGB appearance colors, standard border styles, default values, hierarchical names, missing appearances, and page-tree rebuilds are handled without enabling viewer-generated appearances; callers can supply an embedded font for Unicode values.
- Existing combo boxes and list boxes can now update their selections with regenerated appearances from the authoring engine. Separate export and display values, editable custom combo values, single and multiselect lists, selection indexes, cleared selections, top indexes, default values, styling, alignment, supplied Unicode fonts, and page-tree rebuilds remain synchronized without enabling viewer-generated appearances.
- Existing text, checkbox, radio, combo-box, and list-box fields can now reset to their declared default values with coordinated value, selection-index, appearance-state, rich-text, and regenerated normal-appearance updates. Fields without defaults reset to an empty value, while push buttons remain value-less and reject reset requests through the direct editing API.
- Existing fields can now set or clear their user-facing tooltip and export mapping name independently of values and defaults. Metadata changes compose with same-revision value updates and resets across hierarchical field trees instead of overwriting another pending field replacement.
- Existing text, checkbox, radio, combo-box, and list-box fields can now change or remove their typed default values without changing current values. Proposed defaults are validated through the same reset and appearance path before serialization, and default changes compose with metadata and current-value changes in one revision.
- Existing fields can now be removed with coordinated hierarchical field-tree pruning, widget removal from every affected page annotation array, calculation-order cleanup, empty parent pruning, and empty AcroForm plus `NeedsRendering` removal. Tagged removals also prune matching `Form` structure elements, OBJR children, and ParentTree entries while retaining unrelated field structure. Removal composes with page reordering, newly authored fields, and imported AcroForms in one user operation while preserving the original byte prefix.
- Existing pages can now receive newly authored checkboxes, multi-page radio groups, text fields, and both simple-string and separate export/display variants of combo boxes and single or multiselect list boxes. The authoring engine supplies field dictionaries, widgets, values, defaults, metadata, flags, appearances, and font graphs; the AcroForm merger handles hierarchical-name validation, resource collisions, page ownership, existing annotations, and page reordering without importing temporary pages.
- Existing pages can now receive URI, internal-page, named-destination, reset, and PDF-submit push buttons plus unsigned signature fields. Reset and submit actions support all fields, selected named fields, and exclusion lists with definition-order validation; actions, interaction appearances, page destinations, signature flags, widgets, and authored appearance text remain correctly linked through page reordering and complete AcroForm merging.
- Form widgets added to tagged documents now receive final-page `Form` structure elements, OBJR children, ParentTree registrations, widget structure-parent keys, descriptive contents, and tooltip-derived alternate descriptions after AcroForm merging and page reordering. Direct structure roots and direct top-level `Document` elements are normalized to stable indirect identities. Tagged field removal prunes the same structure associations. PDF/A-4 and PDF/UA-2 text-bearing form appearances require embedded TrueType fonts; the incremental add-and-remove PDF/UA-2 form smoke passes qpdf and veraPDF 1.30.2.
- Newly authored fields and imported AcroForms now merge together in the same page-tree operation through one coordinated resource-renaming, hierarchical-name validation, widget-remapping, and AcroForm merge pass. Value, reset, default, metadata, and removal changes requested alongside either source are applied to the completed graph in a follow-up incremental revision.
- Incremental line, rectangle, ellipse, and ink annotations now accept validated dash patterns and serialize matching dashed border styles and appearance operators without changing existing call sites.
- Incremental annotation editing now adds polylines and polygons with authored-equivalent vertex geometry, fills, interior colors, line endings, dash styles, intents, lifecycle metadata, tagged ParentTree integration, and deterministic appearances.
- Incremental annotation editing now adds caret marks through both concise and authoring-compatible API names, including paragraph symbols, colors, opacity, lifecycle metadata, tagged ParentTree integration, and deterministic appearances.
- Incremental image stamps now accept every standard semantic stamp icon name while retaining the caller-supplied image appearance and rejecting undefined icon values.
- Incremental annotation editing now raises the effective catalog version to the exact required level for opacity-aware markup, vertex, caret, stamp, attachment, URI, and redaction features while preserving the original header and rejecting malformed existing version declarations.
- Incremental annotation editing now adds multi-quad PDF 1.7 redaction marks with explicit review appearances, post-redaction fill colors, aligned or repeated replacement text, shared embedded TrueType subsets, baseline Helvetica support outside PDF/A, lifecycle metadata, and tagged ParentTree integration. PDF/A-4 overlays require an embedded TrueType font.
- Incremental line annotations now support every standard start and end symbol, interior colors, arrow or dimension intent, expanded appearance bounds, and matching rendered endpoint geometry.
- Incremental free-text annotations now support left, center, or right alignment, dashed borders, standard free-text intents, two- or three-point callouts, callout ending symbols, expanded bounds, and matching callout appearances.
- Incremental text notes now support every standard icon and workflow state, stable Unicode names, replies to existing or earlier same-update annotations, grouped relationships, and reciprocal popup annotations. Popup additions authorize PDF 1.3 exactly.
- Incremental annotation editing now removes existing named or indexed annotations, including unnamed annotations, while preserving the original byte prefix, cleaning up reciprocal popups, rejecting orphaned replies, detaching shared annotation arrays, and pruning tagged `StructParent`, ParentTree, `Annot`, and OBJR registrations. Removals compose with same-revision additions and named replacements.
- Existing named or indexed annotations, including unnamed annotations, can now replace contents and lifecycle metadata in place without discarding subtype-specific entries or appearances. Tagged content updates synchronize the structure element alternate description and reject empty accessible descriptions, conflicting update and removal requests fail before serialization, and popup dictionaries must be edited through their parent annotations.
- Incremental annotation editing now adds URI, page, and named-destination links with the complete authored appearance, quad geometry, destination-view, contents, and lifecycle-metadata model. Links participate in tagged ParentTree updates and annotation permission enforcement without emitting unused appearance objects, while authoring and editing share URI and quad validation.
- Incremental annotation editing now places existing embedded files on pages with standard file-attachment icons, deterministic appearances, colors, contents, and lifecycle metadata. Embedded-file lookup follows bounded aliases, rejects malformed or case-insensitively duplicate name-tree entries, and reuses the final file-specification identity while tagged files receive structure alternate text.
- Incremental notes, text markup, free text, lines, rectangles, ellipses, ink, and image stamps now accept the same annotation flags, author, subject, creation date, and modification date metadata as authored annotations without changing existing call sites.
- Incremental highlights, underlines, strikeouts, and squiggles now accept multiple text quadrilaterals, emit exact `QuadPoints` hit geometry and tight bounds, and draw independent rotated runs in their appearance streams.
- Incremental page editing now inserts and appends pages with raw PDF content streams while preserving the complete original byte prefix and every existing page presentation edit. Content-bearing additions to existing tagged documents remain fail-closed until matching structure can be supplied, while blank-page insertion stays supported.
- Incremental page editing now appends raw content after existing streams without rewriting them, explicitly replaces or removes page content, coalesces repeated appends deterministically, and applies the same operations after imported-page graph remapping. Existing tagged documents reject unstructured content updates in every build path.
- Incremental page insertion now accepts typed content builders and transfers their complete authored resource graphs through the shared page importer. Standard fonts retain PDF 1.0, alpha images upgrade exactly to PDF 1.4, optional content imports its catalog dependencies at PDF 1.5, and all other validation failures remain visible instead of being mistaken for version retries.
- Selected-page imports now authorize a higher source PDF version even when no unrelated pending feature already requested a version upgrade, closing a path that could previously import newer page features into an older document without a catalog version declaration.
- Incremental page editing can now retarget existing modern Unicode name-tree destinations and legacy catalog destinations without renaming them, so existing links, bookmarks, and open actions follow the new target. Existing and newly inserted target pages are supported with byte-preserving output.
- Incremental page editing can now clear document open actions and complete page-label trees in ordinary revisions or page-tree rebuilds. New label ranges added after a clear replace the prior ranges in the same byte-preserving update.
- Incremental page editing can now clear page layout, page mode, and viewer preferences from the catalog, restoring viewer defaults without rewriting existing bytes. A later setter in the same revision reestablishes the selected preference.
- Incremental page editing can now remove output intents from ordinary documents while retaining the complete original byte prefix. PDF/A-4 documents reject the operation because their archival conformance requires a suitable output intent.
- Incremental page editing can now remove thumbnail images from existing and imported pages in ordinary revisions or page-tree rebuilds, without allocating unused replacement image objects.
- Incremental page editing can now remove explicit user-unit scaling, automatic display duration, transition effects, and annotation tab order from existing and imported pages in either ordinary revisions or rebuilt page trees.
- Incremental page editing can now remove crop, bleed, trim, and art boxes from existing and imported pages. Explicit crop removal remains effective during page-tree rebuilds instead of being undone by inherited page state.
- Incremental page editing can now reset effective page rotation to zero. Ordinary revisions write a local zero to neutralize inherited rotation, while rebuilt page trees omit rotation from the cleared page instead of rematerializing the inherited value.
- Incremental page editing can now remove trailer document information, catalog XMP, and catalog language metadata as one coordinated operation in ordinary revisions or page-tree rebuilds. PDF/A-4 and PDF/UA-2 documents reject removal of their required conformance metadata.
- Incremental page editing can now remove embedded files by case-insensitive name while cleaning both the EmbeddedFiles name tree and catalog `/AF` registrations. Empty containers disappear, page-tree rebuilds retain the removal, and the same name can receive a replacement payload in one revision.
- Incremental page editing can now clear the complete bookmark tree in ordinary revisions or page-tree rebuilds. New bookmarks added after a clear form a fresh hierarchy in the same update rather than reconnecting the historical outline list.
- Bounded indirect-reference readers now enforce the same 32-reference ceiling at stream lengths, tree structural values, diagnostics, graph import, page labels, named destinations, and writer validation instead of allowing an inconsistent extra hop; page, name, and number trees likewise enforce their declared 256-node nesting ceiling exactly.
- Authoring, annotation updates, and page merges enforce the same one-million-entry ceiling as shared name-tree and number-tree readers before rebuilding destinations, embedded files, page labels, ParentTree, IDTree, or extension name trees.
- Authoring and incremental page insertion enforce the shared one-million-page reader ceiling before mutating page state, preventing generated page trees that cannot be reopened.
- Full rewrites retain canonical complete free lists within the cross-reference reader limit and switch to compact sparse table subsections or stream `/Index` ranges above it, preserving high-water allocation state without unbounded gap expansion.
- Incremental writers reject revisions whose sparse cross-reference entry count would exceed the shared per-section reader ceiling before serializing the revision.
- Revision-chain parsing rejects trailer `/Prev` and hybrid `/XRefStm` offsets that point forward in ordinary files, while recognizing the legal forward links from a validated linearized first-page section to its main and hybrid cross-reference data.
- Hybrid `/Size` equality remains mandatory for ordinary revisions, while a validated linearized first-page hybrid stream may declare the smaller object-number range that precedes its primary table.
- Generation-transition checks treat a validated linearized first-page and main cross-reference pair as complementary indexes of one revision instead of misclassifying overlapping entries as an incremental generation change.
- The linearized forward-`/Prev` exception must agree with the declared main-xref `/T` hint within the bounded table-header allowance, preventing a merely plausible linearization dictionary from authorizing an arbitrary forward revision link.
- A forward linearized hybrid `/XRefStm` is accepted only from a first-page trailer whose forward `/Prev` agrees with `/T`, and it must remain inside the declared first-page extent; `/H` continues to identify hint streams as required by the specification.
- Linearization recognition requires the complete parameter dictionary, not merely its object header, to remain within the first 1,024 file bytes.
- Linearization-only cross-reference ordering is enabled only when the PDF header begins the file; the ordinary reader retains its bounded tolerance for prefixed non-linearized documents.
- The declared first-page end `/E` must precede the main cross-reference `/T` hint, preventing an inflated first-page extent from broadening forward-link exceptions.
- Linearization parameter objects must use generation zero and be registered at their exact offset in the first-page cross-reference section; `/O` and the parameter object number must also remain below trailer `/Size`.
- Primary `/H` hint ranges must follow the parameter dictionary and end within `/E`, while an optional overflow range must begin at or after `/E` and end before `/T`.
- The main cross-reference `/T` hint may identify the forward target or a bounded position inside its classic table, but cannot precede the `/Prev` target.
- The first-page table must precede the primary `/H` hint stream; a hybrid `/XRefStm` may legally follow that hint stream but remains bounded by `/E`.
- A shorter linearized `/L` is accepted for appended revisions only when that exact original prefix ends as a complete PDF, and its own `startxref` identifies the section receiving linearization ordering.
- Sparse full rewrites retain every known inherited free-object entry and generation in both classic tables and cross-reference streams instead of preserving only the high-water sentinel.
- Bounded Flate and LZW decoding accounts for PNG predictor row selector bytes before reconstruction, so exact cross-reference row limits remain resistant to decompression expansion without rejecting legal predicted streams.
- Multi-filter decoding bounds intermediate stages by their existing encoded footprint while applying the configured ceiling exactly to final output, allowing legal ASCII-wrapped compressed streams without permitting intermediate expansion beyond source storage.
- Final `startxref` declarations reject offsets at or after their own marker, requiring the final cross-reference target to precede the declaration physically.
- Final `startxref` markers and offsets require PDF whitespace token boundaries, rejecting embedded marker substrings and concatenated offsets or end markers.
- Final `startxref` parsing treats comments as PDF trivia around the numeric offset while retaining strict token boundaries and rejecting any data after the final `%%EOF` marker.
- Final-marker discovery ignores `startxref` text inside trailing PDF comments so legal commentary cannot eclipse the actual declaration.
- Revision-chain parsing requires trailer `/Size` to remain nondecreasing across incremental revisions and requires hybrid companion streams to agree with their primary trailer, preserving the document's object-number high-water mark.
- Cross-reference history enforces legal generation transitions: active updates retain their generation, deletion advances it, and free-object reuse retains the free generation, preventing stale or invented identities from replacing current objects.
- Classic and stream cross-reference entries reject generation 65,535 for in-use objects because that terminal generation is permanently retired.
- Canonical object serialization likewise rejects indirect object declarations at retired generation 65,535 while continuing to permit references that resolve to a free null identity.
- Classic tables retain compatibility with a free entry exactly at the trailer `/Size` boundary while rejecting free object numbers beyond that boundary.
- Compressed-object loading rejects object stream containers with nonzero generations, preserving the generation-zero identity required by object streams.
- Cross-reference stream type 2 rows reject object stream zero, self-containing compressed objects, and object stream numbers outside the declared `/Size` range during section parsing.
- Object-stream loading validates header membership across revision history, allowing legitimately superseded members without letting never-registered headers invalidate or masquerade as current compressed objects.
- Historical object-stream membership is scoped to revisions using the same physical object-stream offset and generation, so a rewritten container cannot authorize inactive headers from its older byte version.
- Encryption and decryption resolve aliased stream type names before applying signature, metadata, embedded-file, and cross-reference exemptions, preserving cleartext metadata and selecting the correct crypt method through multi-hop `/Type` references.
- Selected-page imports retain named destinations whose page target is reached through a bounded multi-hop indirect alias chain, with cycle and depth rejection based on final page identity.
- Corrected selected-page corpus reporting so deliberate fail-closed source-validation rejections are counted separately from unexpected importer failures; the complete 2,236-file gate now reports 2,149 successful imports, 64 validation rejections, 21 unsupported dependency cases, two malformed sources, and zero unexpected failures.
- Full rewrites now reject application-defined, document-information, and encryption trailer graphs that reference xref or object-stream containers omitted from rewritten output, preventing dangling references in otherwise successful rewrites.
- Reverified the compressed full-rewrite corpus baseline at 2,231 successful rewrites out of 2,236 files, with only five intentional strict source rejections and no new failure from final trailer-graph validation.
- Full rewrites now validate all emitted object graphs, including unreachable live objects, against the final writable object set.
- Cross-reference stream objects now reject nonzero generations.
- Standalone cross-reference streams require an exact generation-zero in-use entry for their own object and offset; hybrid streams may receive that registration from either companion section in the same revision.
- Cross-reference stream `/Index` ranges now reject disordered or overlapping subsection declarations before row decoding.
- Cross-reference streams validate row counts before filter decoding and cap decoded output at one byte beyond the exact `/W` and `/Index` length, preventing oversized declarations and compressed payloads from reaching the generic stream limit.
- Primitive object serialization now rejects direct and container-nested streams while retaining valid indirect stream output.
- Logical round-trip comparison now canonicalizes resolved values as indirect objects so stream comparison remains valid under strict stream serialization.
- Stream decoding now confines TIFF and PNG predictor reversal to Flate and LZW filters instead of applying predictor-like parameters to unrelated filters.
- Signature discovery now uses the merged trailer chain for `/Root`, supporting incremental revisions that inherit the catalog reference from an older trailer.
- Detached signature placeholder patching is now scoped to the exact appended signature object, preventing fixed-sentinel collisions with preserved source bytes.
- Non-Unicode text strings now decode PDFDocEncoding special characters correctly instead of treating their bytes as Latin-1, and undefined PDFDocEncoding byte values fail closed.
- Shared name-tree and number-tree readers now reject keys that are not strictly ordered across leaf and intermediate traversal, empty child arrays, and `/Limits` bounds that disagree with actual descendant keys.
- Name-tree and number-tree structural arrays, keys, and bound endpoints can now be valid indirect objects, including nested `/Kids`, leaf `/Names` or `/Nums`, and `/Limits` arrays; every non-root node must provide its required bounds.
- Page-tree traversal resolves indirect type, child-array, and count values; requires exact descendant counts and `/Page` versus `/Pages` identities; rejects branch entries on leaves and empty non-root branches; and verifies every reciprocal `/Parent` reference by complete identity.
- Page editing resolves valid indirect catalog type and inherited rotation values while retaining strict catalog identity and multiple-of-90 rotation checks.
- Signature discovery bounds total field traversal and resolves valid indirect field types, partial names, filter names, transform methods and parameters, locked-field strings, permissions, actions, and byte-range integers.
- Signed field values and catalog certification targets must resolve to dictionaries declaring `/Type /Sig`; valid indirect signature-type names remain supported across discovery and permission enforcement.
- Signature references validate optional `/Type /SigRef` identity, and FieldMDP Include or Exclude transforms require their `/Fields` arrays.
- Detached signing resolves indirect field names, types, lock scalars, signature flags, seed constraints, timestamp controls, and certificate evidence throughout collision checks, lookup, rewriting, and validation; all signer field traversals are bounded, and existing signed values must be typed `/Sig` dictionaries.
- Detached signing reuses the signature reader's certification parser instead of maintaining a weaker duplicate, and tagged signature creation resolves indirect structure-parent next-key integers.
- Tagged page imports resolve valid indirect destination `/ParentTreeNextKey` and source page `/StructParents` integers before allocating and remapping structure-parent keys.
- AcroForm merging resolves indirect form-level appearance flags, signature flags, default appearance strings, and quadding values while preserving resource renaming and inherited field defaults.
- Imported graph transforms retain reverse source identities so indirect field types and default appearances, named-destination strings, legacy destination names, and tagged structure scalars can be interpreted during collision remapping.
- Selected-page named-destination dependency discovery resolves bounded indirect scalar chains and rejects cycles instead of overlooking split targets hidden behind indirect strings or names.
- Tagged merges resolve bounded indirect chains for structure-root types, document and child roles, ParentTree and IDTree structure scalars, structure IDs, and nested class-name arrays while remapping role, class, ID, and parent-key collisions; cycles fail closed.
- Complete outline imports resolve bounded indirect root counts, identities, titles, colors, flags, destinations, actions, and page modes; bookmark lists require exact parent identity, reciprocal previous and next links, consistent endpoints, unique items, and bounded traversal.
- Page-label editing resolves bounded indirect style, prefix, and start-number chains, strictly decodes prefix text, preserves effective labels through imports and reordering, recompresses consecutive ranges, and rejects malformed text, cycles, invalid starts, and arithmetic overflow.
- Complete catalog imports resolve bounded indirect chains for catalog dictionaries, arrays, and validated scalar values, including output intents and viewer preferences, while rejecting cycles and excessive depth.
- Selected-page imports resolve bounded indirect chains for page presentation values, content arrays, rectangles, resource categories, color spaces, functions, patterns, shadings, graphics states, fonts, optional-content properties, and associated dictionaries.
- Name, number, and page trees now resolve bounded indirect structural chains, and incremental annotation editing plus incremental and full-rewrite writer validation accept the same legal catalog, tagged-structure, namespace, annotation-array, and metadata indirection while rejecting cycles and excessive depth.
- Signature discovery and detached signing now resolve bounded indirect chains consistently for signature dictionaries, transform parameters, byte ranges, field values, and seed constraints, closing the remaining one-hop validation paths while retaining exact signature-reference identity checks.
- Structural diagnostics now resolve bounded multi-hop catalog roots consistently with writer validation, require the root dictionary to declare `/Type /Catalog`, and report invalid types, cycles, and excessive depth as catalog findings.
- Document and editor stream decoding now resolve bounded indirect chains for `/Filter`, `/DecodeParms`, filter and parameter arrays, and predictor scalars while rejecting cycles and excessive depth; encrypted explicit `/Crypt` selection applies the same rules across existing and pending incremental objects.
- Password authentication resolves bounded multi-hop trailer `/Encrypt` aliases, rejects cycles and excessive depth, and keeps every bootstrap alias uncompressed through incremental and full rewrites so authentication remains possible.
- Shared page-tree traversal resolves bounded trailer `/Root` and catalog `/Pages` alias chains to their final indirect identities, allowing edits through legal aliases while rejecting root and page-root cycles.
- Full metadata removal follows complete `/Info` and catalog `/Metadata` alias chains, deletes every unshared link, and updates the final catalog identity behind an aliased trailer root while preserving shared-object fail-closed behavior.
- Incremental updates treat freeing any live member of an inherited `/Info` alias chain as document-information removal and restore the inherited registration when that object is replaced in the same update.
- Signature discovery compares the final resolved identities of field `/V` and catalog `/Perms /DocMDP` chains, correctly classifying certification signatures hidden behind distinct aliases to the same `/Sig` dictionary.
- Detached signing resolves final AcroForm, field-array, field, and permissions identities before traversal or replacement, preserving legal alias chains while updating the actual form and field objects.
- Tagged detached signing updates the final structure-root and parent-tree dictionaries behind aliases instead of overwriting alias nodes.
- Tagged detached signing appends through final structure-kids arrays and updates final top-level element dictionaries while preserving their outer aliases.
- Shared name-tree and number-tree traversal follows bounded node alias chains while retaining cycle and reused-node detection across every identity in each chain.
- Incremental tagged-annotation editing replaces the final structure-root dictionary behind catalog aliases while preserving the original alias chain.
- Incremental annotation editing appends to final indirect annotation arrays and detects duplicate annotations by final identity without replacing page-level aliases.
- Shared annotation arrays remain copy-on-write: only unshared final arrays are updated in place, while pages sharing the same final identity detach before appending.
- Tagged page merging updates final destination structure-root, ParentTree, and Document-element identities and seeds imported roots and Document elements by final source identity.
- Complete outline imports resolve final source and destination bookmark-root identities, preserving catalog-level outline aliases during destination updates.
- Bookmark traversal validates cycles, endpoints, parents, and previous links by final item identity; destination updates preserve outer list aliases while replacing final bookmark dictionaries.
- Partial AcroForm imports select widgets, prune fields, retain calculation-order entries, apply overrides, and detect reused fields by final identity while preserving outer field aliases.
- Tagged page removal rewrites final structure-root, ParentTree, kids-array, element, and page identities while preserving outer structure references.
- Optional-content merging updates the final destination `/OCProperties` dictionary behind aliases instead of flattening the catalog value.
- Named destinations, embedded files, and other name-tree category merges update the final catalog `/Names` dictionary while preserving its outer aliases.
- AcroForm merging updates the final destination form dictionary behind aliases, and calculation-order membership is checked by final field identity.
- Transplanted XFA form calculation-order entries use the same final field identities as ordinary complete and partial form merges.
- Structure ID-tree values, indirect root-kids arrays, and top-level parent links resolve bounded alias chains during tagged merges and direct-root normalization.
  - Optional-content registration, visibility, radio groups, order arrays, and selected-page pruning compare final OCG identities across alias chains.
  - Incremental document-information removal follows pending alias replacements, so freeing an object from the superseded `/Info` chain does not remove the redirected live registration.
  - Encrypted incremental writes derive bootstrap objects from the pending `/Encrypt` alias chain, keeping redirected encryption dictionaries direct and clear even when object streams are enabled.
  - Direct tagged-document normalization derives the retained structure-element identity from final child `/P` references, preserving parent aliases instead of overwriting them.
  - Incremental annotation validation compares final page, reply, popup, and parent identities, accepting registered and reciprocal links expressed through legal alias chains.
  - Imported-page annotation registration and validation apply the same final-identity rules to duplicates, page ownership, replies, popups, and reciprocal parent links.
  - Shared page-tree traversal resolves bounded alias chains for every `/Kids` node and reciprocal `/Parent` link, using final identities for cycle and reuse checks.
  - Action-graph validation detects cycles and reused actions by final identity even when separate `/Next` aliases conceal the same action dictionary.
  - Document security-store pools and VRI membership compare final validation-stream identities across aliases.
  - Rich-media asset, configuration, view, instance, and activation registrations compare final identities across alias chains.
  - Imported article-thread bead rings validate thread membership, cycles, and reciprocal next/previous links by final identity.
  - Document-part hierarchy traversal resolves root, child, and reciprocal parent alias chains and detects reused final nodes.
  - Collection folder traversal resolves root, child, sibling, and reciprocal parent aliases while detecting cycles and reused final folders.
  - Page-navigation graphs bound traversal by final node identity so alias cycles and shared nodes terminate deterministically.
  - Selected-page form discovery resolves indirect widget subtype chains before pruning the partial AcroForm hierarchy.
  - AcroForm procedure sets and optional-content dependency scanning resolve indirect name chains, including Form, Pattern, OCG, and OCMD types; optional-content group and usage-application scalars follow the same rule.
  - Tagged page removal resolves indirect MCR and OBJR type names before pruning removed-page structure references.
  - Direct tagged-root normalization follows aliased child parent links to their final structure-root identity.
  - Full rewrites resolve indirect structural-stream type names before discarding obsolete cross-reference and object streams.
  - Selected tagged-page pruning retains IDTree entries whose structure elements are reached through indirect aliases.
  - Tagged page removal resolves indirect page structure-parent keys before pruning ParentTree mappings.
  - Optional-content merges resolve indirect default and alternate configuration base-state names.
  - AcroForm qualified-name collection resolves indirect field partial-name strings during complete and selected-page merges.
  - Combined tagged page removal and document merging reads rewritten top-level structure elements by final identity, preserving outer aliases without reviving pruned children.
  - Signature discovery detects reused AcroForm fields by final identity when separate aliases target the same field dictionary.
- Tagged-annotation traversal updates final top-level structure-element identities and validates reciprocal parent links after bounded alias resolution.
- Stream parsing now resolves bounded multi-hop indirect `/Length` chains and reports reference cycles or excessive depth deterministically before consuming payload bytes.
- Compressed-object loading now resolves bounded indirect object-stream `/Type`, `/N`, and `/First` scalars after cross-reference bootstrap, while cross-reference stream fields remain deliberately direct because no document resolver exists yet.
- Added a standalone, UI-free .NET 10 document-engine project and test project that will replace KillerPDF's PdfSharpCore document pipeline without replacing PDFium rendering.
- Complete-document imports validate PDF 2.0 requirement penalties, version descriptors, signature constraints, and encryption constraints while preserving extensible registered requirement and requirement-handler subtype names.
- Catalog extension merging no longer restores namespace entries whose values resolve to null after the extension merge deliberately skips them.
- Complete-document imports validate PDF 2.0 document security stores, including typed DSS and VRI dictionaries, uppercase signature digests, indirect certificate and revocation streams, VRI membership in document-wide validation pools, creation dates, and timestamp exclusivity.
- Imported Web Capture commands reject reserved flag bits while retaining converter-specific command settings as extensible dictionaries.
- Shared PDF date validation now requires timezone information to follow complete date and time components and requires the apostrophe separator before numeric timezone minutes across imports, full rewrites, and incremental updates.
- Full and incremental writer tests now cover every legal PDF date precision, leap days, UTC, and signed timezone offsets so strict metadata validation remains compatible with valid partial dates.
- Canonical object serialization now rejects indirect declarations and references using reserved object number zero instead of emitting syntax that conflicts with the cross-reference free-list head.
- Imported page-level output intents now receive the same identity, subtype, profile, reference, and mixing-hint validation as catalog output intents.
- Imported annotation language identifiers now require decodable string values and valid BCP 47 syntax.
- Imported annotation replies now require indirect, typed annotation targets instead of accepting arbitrary direct dictionaries.
- PDF text decoding now consistently supports UTF-16BE and PDF 2.0 UTF-8 signatures with strict malformed-input rejection across language tags, annotation states, structure namespaces, and signature metadata.
- Signature discovery now has explicit coverage for PDF 2.0 UTF-8 field names in existing AcroForm trees.
- Tagged incremental annotation editing now validates every indirect typed namespace entry and optional schema, resolves indirect UTF-8 namespace URI strings, and rejects duplicate PDF 2.0 namespace registrations.
- Tagged incremental annotation editing now validates every retained ParentTree value as a role-bearing structure element or a legal array of indirect structure elements with explicit null gaps before rebuilding the tree.
- Tagged incremental annotation editing now requires every top-level structure element to carry a name-valued role and resolves indirect role names when locating the document container.
- Tagged incremental annotation editing now verifies reciprocal parent links from every indirect top-level structure element to the actual structure-tree root.
- Tagged incremental annotation editing now requires `/StructTreeRoot` to resolve to a dictionary with `/Type /StructTreeRoot` before any structural update.
- Tagged incremental annotation editing now resolves legal indirect `/ParentTreeNextKey` integers while retaining negative-value and overflow protection.
- Tagged incremental annotation editing now rejects stale or semantically empty existing document-element kids, retaining only nonnegative MCIDs, role-bearing elements, marked-content references, and object references before appending annotations.
- Incremental annotation editing now validates retained page annotation entries as indirect typed dictionaries with subtype names and finite four-number rectangles before rewriting `/Annots` arrays.
- Incremental annotation editing now rejects retained annotations whose `/P` entry identifies a different page before appending to that page's `/Annots` array.
- Incremental annotation editing now rejects duplicate indirect annotation identities within a retained page `/Annots` array.
- Imported page annotation arrays now reject duplicate indirect annotation identities before remapping their object graphs.
- Imported annotations with `/P` ownership links now must identify the page being imported, preventing unrelated page dictionaries from entering the remapped annotation graph.
- Imported markup `/Popup` links now require the popup's reciprocal `/Parent` reference to identify the same markup annotation.
- Imported popup `/Parent` links now require the markup annotation's reciprocal `/Popup` reference to identify the same popup.
- Imported reply, popup, and popup-parent references now must identify annotations registered in the imported page's own `/Annots` array.
- Imported page annotations now strictly decode `/NM` text and reject duplicate annotation names on the same page.
- Imported annotation contents, names, authors, and subjects now use strict PDF text decoding and reject malformed UTF-16BE or PDF 2.0 UTF-8 payloads.
- Incremental annotation editing now strictly decodes retained `/NM` text and rejects duplicate annotation names before appending.
- Incremental annotation editing now checks generated KillerPDF annotation names against retained `/NM` values before writing, preventing object-number-based name collisions.
- Incremental annotation editing now strictly decodes retained contents, authors, and subjects before extending page annotation arrays.
- Incremental annotation editing now validates retained modification and creation dates, nonnegative flags, and bounded opacity before extending page annotation arrays.
- Incremental annotation editing now validates retained structure-parent keys, appearance dictionaries, nonempty appearance-state maps, and `/AS` selection consistency before appending.
- Incremental annotation editing now validates retained color arrays, legacy borders, border-style dictionaries, dash patterns, and quadrilateral geometry before appending.
- Incremental annotation editing now validates retained BCP 47 language tags and requires reply targets to be typed annotations registered on the same page.
- Incremental annotation editing now requires retained popup and markup annotations to be registered on the same page with reciprocal `/Popup` and `/Parent` links.
- Incremental annotation editing now validates retained rich text containers, intent names, reply types, and paired text-annotation state models and values.
- Incremental annotation editing now requires retained appearance streams to be Form XObjects with finite bounding boxes and matrices and dictionary-valued resources.
- Incremental annotation editing now detaches shared indirect `/Annots` arrays before appending page-specific annotations so other pages retain their original arrays.
- PDF strings now reject undefined lexical-form enum values instead of silently serializing them as literal strings.
- PDF arrays and dictionaries now reject null object references at construction and direct callers to use the explicit PDF null object.
- Full and incremental writers now traverse complete custom document-information graphs and reject stale nested references instead of validating only the standard metadata fields.
- Unrelated low-level incremental updates preserve malformed inherited standard information fields byte-for-byte while still validating graph liveness; strict field semantics apply when callers replace `/Info` or perform a full rewrite.
- Full and incremental writers now preserve extension-defined trailer entries inherited from older revisions by using the merged trailer view with newest-value precedence, while keeping cross-reference-stream keys revision-local.
- Full and incremental writer coverage now explicitly verifies newest-value precedence when an extension-defined trailer key is redefined across revisions.
- Merged-trailer coverage now verifies that a hybrid revision's primary trailer wins over its companion cross-reference stream for duplicate application keys.
- Cross-reference traversal coverage now verifies that revision chains beyond the configured 1,024-revision bound fail before unbounded parsing.
- Cross-reference traversal coverage now verifies that hybrid `/XRefStm` pointers cannot reuse an already visited primary-section offset.
- Cross-reference streams now reject `/XRefStm`; hybrid companion pointers remain confined to classic trailers.
- Hybrid companion cross-reference streams now reject their own `/Prev` chain so revision history remains owned by the primary classic trailer.
- Object-stream resolution now verifies every declared object number and index against a matching compressed cross-reference entry for the same object stream.
- Classic and stream cross-reference sections now require object 0 to remain free at generation 65,535.
- Free cross-reference entries now require next-free pointers below trailer `/Size`, preventing impossible inherited free-list heads.
- Merged cross-reference tables now reject reachable free-list cycles and heads that identify active or missing objects.
- Merged revision history must define object 0 as the generation-65,535 free-list head even when newer sparse sections omit it.
- Valid trailer `/ID` pairs retain the same first, permanent identifier across incremental revision history while allowing the second revision identifier to change.
- Every declared trailer `/ID` value must be an array of exactly two strings instead of allowing malformed identifier state to bypass revision-history validation.
- Incremental revision history cannot introduce encryption after unencrypted bytes already exist; existing encryption bootstrap references may still redirect through bounded clear aliases to the same dictionary.
- Full rewrites now traverse the bounded, post-policy catalog object graph and reject stale generations instead of emitting dangling structural references, while the low-level incremental builder remains available for forensic revisions.
- Imported printer-mark and trap-network annotations now validate their required flags and appearances, optional identifiers and tracking state, font dependencies, printer-mark styles and Separation colorants, and trap-network process models, spot colorants, indirect regions, and descriptions.
- Imported image and form XObjects now validate OPI 1.3 and 2.0 version dictionaries, external file specifications, required legacy geometry, paired modern crop geometry, bounded crop regions, color and tint operands, included-image dimensions and quality, and defined ink declarations.
- Imported image and form XObjects now also validate external reference-page dictionaries, alternate images, metadata, optional-content membership, associated files, geospatial measurements and point tuples, identifiers, names, modification dates, and the defined form type.
- Imported PostScript XObjects validate optional LanguageLevel 1 fallback streams and support the standard legacy `/Subtype /Form` plus `/Subtype2 /PS` representation.
- Imported viewport and XObject point-data collections now share full cloud identity, column-name, tuple-width, and predefined numeric-coordinate validation.
- Imported geospatial measures now require typed EPSG or ASCII WKT coordinate systems, correctly treat bounds and local points as optional, match local and global point counts, and validate display-unit triples and projected-coordinate matrices.
- Imported rectilinear number formats now validate object identity, fractional display and label ordering names, decimal precision or fractional denominators, fixed-denominator flags, separators, and label spacing, including the final-unit restriction on fractional controls.
- Imported 3D annotations now validate activation and deactivation lifecycle controls, referenced 3D streams, instantiation scripts, preset and default view selectors, named views, camera matrix sources, transformation matrices, and U3D view-node paths.
- Imported 3D animation styles now validate object identity, name-valued subtypes, integer play counts, and positive finite time multipliers while preserving forward-compatible unknown animation styles.
- Imported 3D views now validate perspective and orthographic projections, clipping and scaling controls, RGB backgrounds, render modes, lighting schemes, cross-section geometry and colors, and per-node opacity, visibility, and transformation state.
- Imported RichMedia graphs now validate content and settings identities, asset name trees, registered indirect file specifications, indirect configurations and instances, defined media subtypes, animation controls, presentation styles and flags, floating-window dimensions, and aligned window positions.
- RichMedia Flash parameters now validate binding modes, required material names, state payloads, cue-point identities, names, timing, and event actions; activation configuration, view, and script references must belong to the content's registered collections.
- Imported external-data dictionaries now validate defined 3D markup and measurement-association subtypes, required annotation and view targets, 16-byte artwork checksums, projection-only measurement links, and indirect measurement references.
- Imported sound objects now distinguish inline sample data from external self-describing files, require positive inline sampling rates, and validate external file specifications; redaction overlay text requires a default appearance unless a replacement appearance is supplied.
- Imported movie annotations now validate image-XObject posters, time and time-scale operands, floating-window magnification and screen positions; popup links require indirect popup and markup-parent dictionaries.
- Imported text annotations now validate text-string state models, state values, and icon names; links reject conflicting action and destination targets and validate previous-view actions; free-text annotations validate defined intents and require callout geometry for callout intent.
- Imported caret, square, circle, and free-text rectangle differences now must preserve a non-collapsed inner annotation rectangle.
- Imported 3D view validation now continues past string-valued U3D node paths and bounds indexed default-view selectors to the declared view array. RichMedia instances must match their containing configuration subtype, and windowed presentations require complete width and height bounds.
- Imported page viewports now require finite, non-collapsed bounding rectangles before their geospatial or point-data graphs are imported.
- Imported geospatial measures now require local unit-square control points matching their global coordinate count. Viewport and point-data collections reject empty arrays, duplicate point-data columns, and non-finite predefined coordinates.
- Imported page separation metadata now requires indirect page memberships and a Separation or DeviceN color space containing the declared device colorant.
- Imported page and Form XObject transparency groups now reject Lab and special color spaces prohibited as blending spaces. Page bead arrays are nonempty and repeated ring entries share bounded identity-aware validation.
- Imported production page boxes now reject collapsed rectangles.
- Imported Form XObject bounds and matrices and extended graphics-state line, opacity, dash, font, and soft-mask values now require finite numbers. Soft-mask transfer functions and backdrop component counts are validated against their transparency groups.
- Imported extended graphics-state halftones now validate defined type identities, dictionary-versus-stream forms, type 1 frequency, angle, and spot functions, type 5 component graphs, type 6, 10, and 16 dimensions, transfer functions, names, depth, and cycles while allowing legal shared sub-halftones.
- Imported tiling-pattern steps and matrices and Type 3 font bounds and matrices now reject non-finite numeric values.
- Root and nested page resources now share one recursive validator. Name-valued color-space resources are limited to direct device spaces or Pattern, shading background, function, and mesh-decode dimensions match their color spaces, and tint or shading function input and output dimensions match their callers.
- Imported DeviceN spaces now reject duplicate or prohibited colorants and inconsistent NChannel or process-component declarations. Image color-key masks use bounded integer pairs matching the image color components and sample depth.
- Imported image soft masks now require DeviceGray color spaces, alternate-image collections are nonempty, and Form transparency groups use the shared page-group validator.
- Imported Indexed, uncolored Pattern, Separation, and DeviceN spaces now require device or CIE-based base and alternate spaces instead of recursively accepting special color spaces.
- Added from-scratch PDF 2.0 catalogs, page trees, arbitrary finite page sizes, content streams, graphics-state operations, transforms, paths, Bézier curves, rectangles, rounded rectangles, circles, ellipses, colors, fills, and strokes.
- Added new-document page rotation, crop, bleed, trim, and artwork boxes constrained to the media box, plus bounded user-unit scaling for unusually large or small page formats.
- Completed PDF path construction and painting with both cubic Bézier shorthand forms, close-and-stroke, even-odd fill-and-stroke, and closed nonzero or even-odd fill-and-stroke operators.
- Added complete stroke styling with butt, round, and projecting-square caps; miter, round, and bevel joins; validated miter limits; reusable dash sequences and phases; and solid-stroke reset.
- Added native DeviceCMYK fill and stroke authoring plus validated CMYK base colors for uncolored stencil patterns, complementing existing grayscale and RGB graphics support.
- Added reusable ICCBased Gray, RGB, and CMYK fill, stroke, and uncolored-pattern base colors with embedded profiles, device alternates, strict component validation, scoped page/form/pattern resources, deterministic sharing across pages, and safe output-intent object reuse.
- ICCBased authoring and output intents now accept every standard ICC three-component profile signature allowed by PDF's `/N 3` model, including XYZ, Lab, Luv, YCbCr, Yxy, HSV, HLS, CMY, and `3CLR`, plus four-component `4CLR` profiles.
- Added reusable named Separation spot colors with process-CMYK alternates, tint-transform functions, validated fill and stroke tint values, and scoped page, form, and pattern resources.
- Added reusable CIE L*a*b* fill and stroke color spaces with explicit white and black points, configurable a*/b* ranges, strict component validation, and scoped page, form, and pattern resources.
- Added reusable Indexed Gray, RGB, and CMYK palette color spaces with compact binary lookup tables, one-byte palette indices, strict entry bounds, and scoped page, form, and pattern resources.
- Added reusable CalGray and CalRGB fill, stroke, and uncolored-pattern base colors with explicit white and black points, calibrated gamma and matrices, strict component validation, and scoped page, form, and pattern resources.
- Added all four standard color-rendering intents and validated curve-flatness tolerance controls for predictable screen and print painting behavior.
- Added reusable extended graphics-state resources with independent fill and stroke opacity, all sixteen standard separable and non-separable PDF blend modes, alpha and luminosity soft masks backed by explicitly color-managed transparency-group forms with optional backdrop colors, fill and stroke overprint with both overprint modes, alpha-source selection, text-knockout control, and deterministic sharing within and across pages.
- Added reusable axial and radial gradient shadings with clipping, optional evaluation bounds and matching background colors, antialiasing control, arbitrary strictly ordered Gray, RGB, or CMYK color stops, two-color interpolation, multi-stop stitching functions, extension controls, consistent color-space validation, and deterministic sharing across pages.
- Public value-type inputs now revalidate constructor invariants at their point of use, rejecting default collapsed text quads, gradient stops, shading bounds, pattern matrices, and PDF versions before they can produce invalid output or headers.
- Authored UTF-16BE and UTF-8 strings now use strict encoders across metadata, annotations, forms, links, spot colors, embedded-font mappings, and signatures. Unpaired surrogates are rejected instead of being replaced, and malformed signature, destination, or TrueType name strings fail closed or fall back without identity collisions.
- Catalog language metadata now validates the complete BCP 47 tag structure, including extlangs, scripts, regions, unique variants, unique extensions, grandfathered registrations, and private-use subtags, before writing `/Lang`.
- Catalog language is now mirrored into the XMP Dublin Core language bag, and typed prepress trapping status is written consistently to the information dictionary and XMP.
- Added typed page keyboard-tab ordering for row, column, structure, and annotation-array traversal. PDF/UA-2 pages default to structure order and reject incompatible overrides.
- Incremental page editing can replace keyboard tab order across existing, inserted, and imported pages, with effective-version upgrades to PDF 1.5 or PDF 2.0 as required.
- Incremental page editing can attach typed image thumbnails across existing, inserted, and imported pages, including recursively allocated alpha soft masks and the required PDF 1.4 effective-version upgrade.
- Authored RGBA page thumbnails now participate in feature-version validation, preventing PDF 1.3 output from containing a PDF 1.4 image soft mask.
- Incremental page editing sets typed catalog page layouts and initial page modes with effective-version upgrades for PDF 1.5 two-page and optional-content modes and PDF 1.6 attachment mode.
- New-document authoring now rejects those layout and page-mode values when the selected PDF version predates their specification introduction.
- Viewer preferences share one canonical typed serializer between authoring and incremental editing. Both paths enforce the PDF 1.2 base dictionary, PDF 1.3 reading direction, PDF 1.4 document title, PDF 1.6 print scaling, and PDF 1.7 duplex and tray-selection boundaries.
- Page destinations share one canonical serializer between authoring and incremental editing.
- Incremental editing adds page or Unicode named document-open actions, retaining page identity through reordering and rejecting removed targets or unknown names.
- Incremental editing adds Unicode named destinations for existing or newly inserted pages, preserving existing destination and name-tree state and allowing a new destination to become the open action in the same update.
- Incremental editing adds typed decimal, Roman, alphabetic, or prefix-only page-label ranges for existing and inserted pages, preserving effective labels through page operations and authorizing the PDF 1.3 feature through the catalog version when necessary.
- Incremental editing replaces document information, catalog language, and descriptive XMP metadata in one revision. Existing XMP packets retain PDF/A, PDF/UA, and private schemas while standard element-form or RDF-attribute properties are updated consistently; new packets share authoring's canonical serializer.
- Metadata replacement composes with page reordering and encrypted incremental revisions.
- Explicit incremental catalog presentation and metadata edits take precedence over catalog properties transplanted by a complete-document import in the same revision.
- Attachment validation and object construction are shared between authoring and incremental editing.
- Incremental editing embeds new associated files into existing documents, preserving embedded-files name-tree categories and catalog associations, rejecting case-insensitive name collisions, and raising the effective version to PDF 2.0.
- Incremental attachments preserve conformance boundaries: general PDF/A-4 rejects them, PDF/A-4f and PDF/A-4e allow them, and PDF/UA-2 requires descriptive file-specification metadata.
- Incremental editing authors hierarchical page or named-destination bookmarks with open and collapsed state, RGB color, bold and italic styling, and page-identity tracking through reorder operations.
- New bookmark trees, existing outline lists, and complete imported outline segments share one merged root with reciprocal boundary links, validated counts, and automatic outline-view mode when no explicit page mode exists.
- ICC profile and output-intent validation and object construction are shared between authoring and incremental editing.
- Incremental editing installs a typed destination output profile and PDF/A output-intent dictionary, replacing imported intent state when explicitly requested and authorizing the feature through PDF 1.4 catalog versioning.
- Incremental thumbnail edits reuse one indirect image and soft-mask graph when the same typed image instance is assigned to multiple pages.
- PDF/UA-2 URI and direct-page links now receive descriptive contents, structure-parent keys, ParentTree mappings, standard Link structure elements, and OBJR back-references. A real smoke file passes qpdf and veraPDF UA-2.
- PDF/UA-2 form widgets now require tooltips and embedded fonts for text-bearing appearances, and receive descriptive contents, structure-parent keys, ParentTree mappings, standard Form structure elements, and OBJR back-references. A mixed hierarchical form smoke file passes qpdf and veraPDF UA-2.
- PDF/UA-2 text notes and text-markup annotations now require descriptive contents and receive structure-parent keys, ParentTree mappings, standard Annot structure elements, and OBJR back-references. A combined annotation smoke file passes qpdf and veraPDF UA-2.
- PDF/UA-2 free text, image stamps, visual, caret, redaction, and file-attachment annotations now receive accessible Annot associations, embedded files require descriptions, and internal navigation uses page-associated structure destinations while rejecting unstructured targets. A combined navigation and annotation smoke file passes qpdf and veraPDF UA-2.
- Incremental annotation editing now preserves tagged and PDF/UA structure through new structure-parent keys, Annot elements, OBJR references, rebuilt ParentTrees, and advanced next-key state. A compressed incremental smoke file passes qpdf and veraPDF UA-2.
- PDF/UA-2 encryption now requires accessibility extraction, Formula roles serialize correctly with required descriptions, legacy Note maps to FENote, and legacy inline roles use the PDF 1.7 namespace under validated hierarchy. Mixed-namespace authored and incrementally edited smoke files pass qpdf and veraPDF UA-2.
- Tagged lists now support typed standard numbering attributes. PDF/UA-2 places legacy `Art`, `Quote`, `Note`, `Reference`, and `Code` roles in the PDF 1.7 namespace, enforces a sole root-level Document, numbering for labeled lists, list-item body wrapping, numbered-heading parent and child models, high-level grouping boundaries for inline spans, grouping-container content rules, forbidden list, table, and legacy inline nesting, and regular table rows while rejecting the generic heading role. The combined role smoke exercises both standard namespaces and passes veraPDF. Encrypted compressed incremental annotation edits retain tagged mappings without exposing appended text.
- New-document authoring now enforces feature minimum versions for XMP, structure trees, forms, signatures, annotations, reusable resources, embedded CID fonts, color spaces, transparency, optional content, page geometry and presentation, navigation, viewer state, associated files, revision-6 AES-256 encryption, tab-order generations, PDF/A-4, and PDF/UA-2 instead of emitting invalid down-version files without developer extensions.
- Added reusable Form XObjects for vector artwork, text, images, gradients, nested compositions, and isolated or knockout transparency groups. Forms keep their resources scoped, can be placed repeatedly at natural or scaled sizes, and are stored only once even when reused across pages.
- Added reusable colored and uncolored stencil tiling patterns for fills and strokes, with DeviceGray, DeviceRGB, DeviceCMYK, ICCBased, calibrated, CIE L*a*b*, Separation spot, or Indexed stencil base colors, configurable cells, horizontal and vertical spacing, tiling behavior, pattern-space transforms, nested resources, and deterministic sharing across pages.
- Added PDF optional-content layers with Unicode names, deterministic viewer ordering, initial visibility controls, shared page-property resources, and complete layer-configuration preservation when whole documents are imported into empty destinations. Unsafe partial or combined layer imports now fail explicitly instead of leaving content tied to a missing catalog configuration.
- A sole complete layered document can now be added to an occupied unlayered destination: existing content remains always visible, imported page resources retain their optional-content references, and the source configuration is installed intact.
- Page editing can now import an explicitly selected page subset as one ordered batch. Direct links among selected pages are remapped even when the selection is reordered, self-links remain intact, duplicate selections are rejected, and dependencies on omitted pages continue to fail closed. Authenticated AES-256 subsets are decrypted with the source key and re-encrypted with a distinct destination key without exposing page content.
- Selected-page AcroForm and structure pruning maps now preserve complete indirect identity end to end, including generation numbers, so stale references cannot receive rewritten dictionaries belonging to the active generation.
- Page, name, and number tree cycle and reuse detection now tracks object generations as well as numbers, so stale self-references are diagnosed as null references instead of false cycles.
- Partial AcroForm imports match selected widgets, field-tree nodes, retained fields, and calculation-order entries by object number and generation, removing stale `/CO` references instead of importing null entries.
- Complete AcroForm merges also validate `/CO` as indirect references to fields reachable from each form’s `/Fields` tree, removing stale source generations and rejecting invalid destination or out-of-tree entries.
- Selected-page graph import, tagged structure pruning, IDTree filtering, optional-content configuration pruning, and named-destination traversal now compare complete indirect identities. Stale generations resolve as null or are removed instead of being mistaken for omitted pages, retained structure elements, layers, or destinations.
- Imported page `/Annots` and `/AF` arrays are rebuilt from resolved dictionaries, removing stale or explicit null entries and rejecting invalid value types. Complete catalog `/AF` merges apply the same validation and omit empty stale-only registrations.
- Tagged merges validate structure-root `/Namespaces`, `/AF`, and `/PronunciationLexicon` collections as dictionary arrays, removing stale source entries and rejecting invalid destination state instead of serializing null elements.
- Embedded-files name-tree merges require file-specification dictionaries and omit stale-only source trees. Tagged ParentTree values must resolve to arrays or structure dictionaries, while IDTree values must be indirect structure-element references; stale required mappings fail before remapping instead of becoming null.
- Name-tree and legacy catalog named-destination merges require destination arrays or dictionaries with defined fit modes, exact operand counts, numeric coordinates, and valid page references; stale source values are removed before collision handling and stale-only containers are omitted.
- Selected tagged conformance imports validate metadata streams, language strings, viewer-preference dictionaries, output-intent dictionary arrays, and catalog version names while omitting stale optional values. Catalog extension merges require developer-extension dictionaries and omit stale-only namespaces.
- Complete AcroForm merges and sole-form transplants validate field arrays, scalar form properties, reachable calculation-order references, resource categories, procedure sets, default-resource entries, XFA stream or packet-array structure, and the catalog `/NeedsRendering` flag, rejecting stale dependencies that resolve to null. Ordinary sole forms use the full merge pipeline while the XFA-only transplant path retains valid rendering state. Imported page contents, inheritance, resources, metadata, thumbnails, article bead dictionaries and geometry, transitions, actions, piece-information application dictionaries and required modification dates, separation pages, colorants, and color spaces, viewport data, timing, units, structure keys, tab order, and modification dates likewise validate their required PDF object types, while private extension keys remain compatible.
- Sole complete-document transplants validate standard catalog names, strings, streams, arrays, booleans, dictionaries, and open actions before importing them, preventing stale standard properties from becoming null while retaining unknown extension entries.
- Sole complete-document transplants require trailer `/Info` to resolve to a dictionary, validate every standard text field and defined `/Trapped` state, and preserve custom entries.
- Tagged merges validate private structure-root references, role-map names, and class-map attribute dictionaries or arrays before importing them, rejecting stale extension and mapping values without restricting valid direct extension data.
- Tagged merges require every top-level structure-root kid to resolve to a structure-element dictionary, rejecting scalar and mistyped `/K` entries before graph remapping.
- Tagged merges validate an explicit structure-root `/Type` as `/StructTreeRoot` for both source and destination trees.
- Tagged `Document` element merges validate child MCIDs, structure-element role names, MCR identifiers and pages, and OBJR object and page dictionaries before combining their `/K` arrays.
- Tagged merges require namespace dictionaries to declare `/Type /Namespace`, a text `/NS` identifier, and a dictionary `/Schema` when present.
- Tagged ParentTree arrays preserve legal explicit null gaps while rejecting stale indirect entries, and every retained mapping dictionary must be a role-bearing structure element.
- Tagged IDTree entries must point indirectly to role-bearing structure elements whose `/ID` bytes match the registered name-tree key.
- Catalog developer-extension namespaces require a `/BaseVersion` name, a nonnegative `/ExtensionLevel` integer, and a string `/URL` when present.
- Imported output intents validate `/Type`, the required `/S` subtype, descriptive strings, and destination ICC profile streams with defined component counts, color-space alternates, and ordered range bounds for both selected tagged imports and sole complete transplants.
- Referenced output profiles validate mutual exclusivity with embedded profiles, 16-byte checksums, ICC version, profile color-space and name strings, nonempty URL arrays, URL file-system identities, address strings, and colorant-table containers.
- PDF 2.0 output-intent mixing hints validate printing-order names and bounded solidity values while rejecting obsolete dot-gain data. Spectral-data dictionaries require stream values and cannot redefine a colorant already present in solidities.
- Imported viewer preferences validate boolean display flags, defined page modes, reading directions, page-boundary areas, print-scaling and duplex values, ordered nonnegative print-page ranges, positive copy counts, and name-only enforcement arrays.
- Imported catalog metadata streams validate `/Type /Metadata` and `/Subtype /XML` when those entries are present.
- Imported catalog `/Lang` strings must decode cleanly and satisfy the same complete BCP 47 validation used by new-document authoring.
- Imported catalog `/Version` names must use defined PDF version syntax rather than merely being name objects.
- Imported catalog `/PageMode` and `/PageLayout` names must use defined standard values, including the bookmark-merge path.
- Complete catalog transplants validate URI base strings and every standard MarkInfo boolean, including `/UserProperties`, for tagged and untagged sources.
- Complete catalog transplants validate open destinations, catalog additional actions, recursive action dictionaries, and required local, remote, or embedded navigation, launch, URI, named, JavaScript, form submission, form reset, data import, hide, sound, movie, transition, thread, rendition, 3D-view, rich-media, and optional-content-state action operands, rejecting empty destinations, stale next actions, mistyped options, and cyclic or reused action graphs.
- Action graphs now reject undefined subtype names and validate PDF 2.0 `/GoToDp` actions as indirect references to typed document-part dictionaries.
- Launch actions validate window behavior and platform dictionaries, including required Windows file strings and optional directory, operation, and parameter strings. Embedded go-to actions recursively validate parent or child relationships, child names, page selectors, annotation names, and nested targets.
- Named actions accept only the four defined page-navigation names. Movie actions support either title strings or typed Movie annotation targets and reject missing, mistyped, or wrong-subtype targets.
- PDF 2.0 local go-to actions validate optional structure destinations with the same fit-mode and operand grammar as page destinations while requiring an indirect typed structure-element target.
- Rich-media-execute actions require indirect RichMedia annotations, typed indirect instances when present, typed command dictionaries, text command names, and scalar or array arguments limited to strings, integers, reals, and booleans.
- Imported page `/AA` dictionaries use the same recursive action validation as catalog and bookmark actions instead of accepting arbitrary dictionary values.
- Imported page boxes require four finite numeric coordinates, rotations require supported multiples of 90 degrees, and duration, user-unit scale, preferred zoom, page identifiers, template-instantiation names, structure-parent keys, tab order, transition dictionaries, viewport arrays, production-box colors, and transparency groups validate their required types, bounds, modes, directions, dimensions, scaling, background flags, measures, and graph structure instead of accepting type-compatible invalid values.
- Incremental page editing can set crop, bleed, trim, and art boundaries through the same typed page-box API used by new-document authoring.
- Production-box edits on pre-PDF-1.3 inputs add a catalog version override, preserving the original header while declaring a version that supports bleed, trim, and art boundaries.
- Incremental page editing can set bounded `/UserUnit` scaling on existing, new, and imported pages and raises the effective catalog version to PDF 1.6 when required.
- Incremental page editing can set automatic display duration and every standard transition on existing, new, and imported pages, sharing canonical transition serialization with new-document authoring and raising the effective version for advanced PDF 1.5 transitions.
- Imported page metadata requires a typed XML metadata stream, while thumbnails require image XObject streams with positive dimensions.
- Imported page resources enforce defined procedure-set names and category-specific object types for graphics states, fonts, properties, color spaces, patterns, shadings, and XObjects. Property lists validate OCG and OCMD dictionaries, policies, group operands, and recursive visibility expressions; color spaces recursively validate calibrated, ICCBased, Indexed, Pattern, Separation, and DeviceN families, ICC alternates, ranges, and metadata, and sampled, exponential, stitching, or calculator functions; graphics states validate line geometry, dash patterns, opacity, overprint, rendering intent, defined blend modes, font pairs, and transparency-group soft masks; fonts validate defined subtypes, encodings, ToUnicode streams, Type 0 descendants, and Type 3 geometry and character procedures; tiling and shading patterns validate modes, geometry, resources, matrices, shadings, and graphics states; shadings validate type, color space, coordinates, bounds, backgrounds, extension flags, antialiasing, and required functions; image and form XObjects validate subtypes, dimensions, component depth, bounds, matrices, resources, and groups.
- Imported calibrated color spaces validate white and black points, scalar or component gamma, calibration matrices, and ordered Lab ranges before resource graphs are imported.
- Imported Indexed color spaces decode bounded lookup streams and require lookup bytes to match the palette size and base-space component count exactly.
- Imported DeviceN color spaces validate NChannel attributes, registered colorant Separation spaces, process components, printing order, solidities, and dot-gain functions.
- Imported image XObjects validate exclusive explicit and soft masks, mask kind, grayscale soft-mask color, matching mask dimensions, component-matched decode arrays, interpolation, rendering intent, embedded soft-mask modes, structure-parent keys, and image-mask color restrictions in addition to dimensions, color spaces, and component depth.
- Imported Form XObjects recursively validate nested resource categories and entries, plus transparency-group identity, group color spaces, isolation and knockout flags, structure-parent keys, typed XML metadata, piece information, and optional-content dictionaries.
- Imported fonts require subtype-appropriate base names and composite CMaps, and validate consistent character ranges and widths, encoding dictionaries and differences, complete finite font-descriptor metrics, mutually exclusive embedded font programs, plus recursive descendant-font and CID system information.
- Imported Type 3 fonts require encodings, complete width ranges, numeric bounding and transformation arrays, stream character procedures, and dictionary resources.
- Imported CID fonts validate finite default widths, horizontal and vertical metric-table grammar, CID ranges, and CID-to-GID mappings.
- Imported extended graphics states validate flatness, smoothness, stroke adjustment, transfer functions, black generation, undercolor removal, halftone objects, and halftone phase arrays.
- Imported mesh shadings require stream form, supported coordinate, component, and flag widths, bounded decode arrays, valid optional functions, and lattice row geometry.
- Imported tiling patterns recursively validate every nested resource category and entry through a bounded resource graph.
- Optional-content group usage validates creator, language, export, print, view, zoom, user, and page-element criteria, including defined visibility states and ordered zoom bounds.
- Imported annotation subtype data validates markup authorship, rich text, reply relationships, required quadrilaterals, free-text appearance and callout data, line endpoints, defined line endings and caption geometry, polygon and polyline vertices, ink paths, popup parents, icon names, sound streams and parameters, redaction presentation, movie file specifications and geometry, 3D payloads, rich-media content, caret symbols, rectangle differences, watermark placement, projection measures, and required print appearances.
- Imported annotation appearance characteristics validate rotation, border and background colors, captions, icon streams, icon-fit scaling and alignment, and text-position modes.
- Imported annotations validate border effects, interior colors, marked and review state-model combinations, line measurement dictionaries, and finite leader-line geometry.
- Imported free-text annotations validate default styles, callout endings, and rectangle differences; sound streams validate object identity; 3D annotations distinguish typed U3D or PRC streams from 3D-reference dictionaries and validate default views and activation bounds.
- Imported rich-media annotations validate asset-tree, defined configuration subtypes, instance assets and parameters, view arrays, settings, and defined activation or deactivation condition structures.
- Imported annotation `/OC` values use the complete optional-content membership validator, and page `/PresSteps` graphs validate typed navigation nodes, durations, actions, and bounded next or previous traversal.
- Imported PDF 2.0 document-part page links require indirect typed `/DPart` dictionaries. Complete-document transplants validate the full indirect `/DPartRootNode` hierarchy, including node names, child arrays, reciprocal parents, page ranges, associated files, document-part metadata value types, cycle and reuse bounds, and prohibited XMP `/Metadata` entries.
- Selected-page imports now reject document-part membership because importing a page without its catalog-level hierarchy would leave an orphaned `/DPart` graph.
- Movie annotation activation dictionaries are validated using their own rate, volume, mode, and control semantics instead of being misclassified as generic action dictionaries.
- Imported viewport measures validate rectilinear scale ratios and number formats, geospatial bounds and point arrays, coordinate-system dictionaries, conversion factors, and supported measure subtypes.
- Imported annotation appearances use the full Form XObject validator, including bounded recursive validation of every appearance resource category and entry.
- Imported page, annotation, piece-information, embedded-file, and document-information dates validate PDF date prefixes, calendar components, and timezone offsets instead of accepting arbitrary strings.
- Imported embedded-file streams validate MIME subtype token syntax and require 16-byte checksum strings when checksums are declared.
- Imported AcroForms validate defined field types, nonnegative flags and limits, bounded quadding, choice-option structure, widget rectangles, defined top-level signature flags, and recursively validated default resources before merging or transplanting fields.
- Complete catalog transplants validate requirement dictionaries and handlers, article-thread roots and metadata, and collection identity, views, schema field definitions, sorting keys and directions, and initial-document values.
- Collection schemas and sort dictionaries validate their own optional object identities instead of misclassifying `/Type` as a user field. PDF 2.0 collection colors validate five RGB triplets, split dictionaries validate defined orientations and positions from 0 through 100, and navigator dictionaries validate identity and layout names.
- PDF 2.0 collection folders require an indirect single-root hierarchy with typed nodes, unique nonnegative IDs, names, reciprocal parents, indirect child and sibling chains, bounded traversal, valid metadata dates, image thumbnails, root-only free-ID ranges, and typed collection-item dictionaries.
- Complete catalog transplants validate Web Capture version 1.0 information, indirect command dictionaries, URLs, levels, flags, posted data, HTTP metadata, and conversion settings. Requirement-handler dictionaries now require defined JS or no-op semantics and subtype-compatible scripts.
- Imported article threads and page bead arrays now require indirect identities, typed thread and bead objects, one owning thread, valid page targets and rectangles, reciprocal next and previous links, bounded traversal, and a ring that closes at its first bead.
- Standard catalog name-tree merges now validate JavaScript action dictionaries, visible and template page references, Form appearance streams, Web Capture page and image content sets, embedded alternate-presentation slideshows, and rendition dictionaries with media viability criteria while retaining unknown extension categories.
- Web Capture content-set source information now validates required source URLs, URL-alias destinations and redirect chains, timestamps and expirations, page-set submission modes, and indirect retrieval-command dictionaries with typed request data.
- Named rendition objects now validate bounded selector trees and media-rendition payloads, including required clips or play parameters, typed media clips, media data, content types, permissions, alternate descriptions, screen parameters, and section parent clips.
- Media rendition play and screen parameters now validate object identity, player containers, volume, controller and autoplay flags, fit modes, repeat counts, duration dictionaries, window modes, RGB backgrounds, opacity, monitor selectors, and required floating-window parameters.
- Media duration, timespan, and clip-section offset dictionaries now validate defined object and subtype names, finite nonnegative seconds, nonnegative frame numbers, marker strings, and begin or end offset structures.
- Media player collections now validate typed player records, required player identifiers, version arrays, and operating-system names. Media permissions validate their typed dictionaries and defined temporary-access modes.
- Media-clip payloads now validate file specifications or XObject streams through their complete object rules, and alternate descriptions require complete language and text string pairs.
- Complete catalog transplants validate catalog piece-information dates and legal-attestation identity, text, modification dates, and defined feature declarations.
- Imported annotations validate their type, required subtype and finite four-number rectangle, common text and nonnegative flag fields, bounded device colors and opacity, numeric quadrilaterals, legacy borders, typed border styles, link highlighting, text-open state, page links, nonnegative structure-parent keys, file-attachment specifications, associated files, and normal, rollover, down, and state-selected appearance Form XObjects with valid bounds, matrices, resources, and groups, plus actions, additional actions, and destinations before their page annotation arrays are rebuilt.
- Page and catalog associated files, embedded-file name trees, structure-root associated files, and pronunciation lexicons share validation for file-specification types, file names, embedded-file dictionaries and streams, stream subtypes and parameters, descriptions, file systems, supplemental dictionaries, and relationship names.
- Tagged merges require catalog `/MarkInfo` dictionaries with boolean `/Marked` and `/Suspects` flags instead of copying stale or mistyped marking state.
- Complete outline imports require source catalog `/PageMode` to resolve to a name before installing it in a destination that has no existing page mode.
- Complete outline imports recursively validate child lists, item reuse, titles, exact explicit-destination syntax, recursive action graphs, associated structure elements, counts, flags, and colors before importing bookmark graphs.
- Additional catalog name-tree categories reject stale indirect values while retaining valid extension-defined direct objects and complete graphs.
- Selected optional-content imports now remove stale indirect references from recursively pruned default and alternate configurations instead of preserving literal null entries in layer-order and usage arrays.
- Optional-content configuration merges reject duplicate source or destination `/OCGs` registrations by complete indirect identity instead of emitting ambiguous layer catalogs.
- Optional-content group registrations must resolve to dictionaries with string names, valid `/OCG` types, dictionary usage data, and name-valued intents; stale or mistyped registrations fail before import.
- Optional-content default and alternate configurations validate their text labels, creator strings, defined list modes, and name-valued intents before merging.
- Optional-content `/ON` and `/OFF` arrays reject duplicate references and groups not registered in `/OCGs`.
- Optional-content configurations validate locked groups, radio-button groups, recursive display order, and usage-application dictionaries against the registered `/OCGs`; selected-page pruning still removes stale order entries safely.
- Selected-page imports preserve name-tree and legacy named destinations that target retained pages, ignore unrelated registrations belonging only to omitted pages, and reject selected-page links whose named target was omitted.
- Added positioned and escaped Latin-1 text with automatic resources for all 14 built-in Type 1 fonts.
- Added text matrices, line leading and next-line movement, character and word spacing, horizontal scaling, baseline rise, and all eight PDF text rendering and clipping modes.
- Added positioned text arrays for built-in Latin-1 and embedded Unicode fonts, enabling deterministic kerning and per-run glyph advances while retaining Unicode mappings.
- Added bounded TrueType/OpenType inspection for names, metrics, embedding permissions, widths, Unicode cmap formats 4 and 12, deterministic glyf/loca subsetting with composite dependencies, full-file CFF-flavoured OpenType embedding, CIDFontType0 and CIDFontType2 descendants, and `ToUnicode` maps.
- TrueType/OpenType inspection now also reads Unicode cmap formats 0, 2, 6, 8, 10, and 13 with bounded byte, subheader, trimmed, or mixed UTF-16/32-bit mappings, ordered groups, supplementary-plane support, `is32` validation, glyph-range validation, and deterministic subtable priority.
- Added supplemental cmap format 14 variation-sequence lookup with default base-glyph fallback, non-default glyph mappings, ordered variation selectors, bounded UVS offsets and ranges, and binary-search resolution.
- Embedded-font authoring now maps supported Unicode variation sequences to a single glyph across page content, forms, visual annotations, and incremental annotation appearances, and preserves the complete sequence in `ToUnicode` maps for extraction and accessibility.
- Embedded fonts now use explicit encoding CMaps and independent character codes when multiple Unicode sequences share one rendered glyph, preserving accurate `ToUnicode` extraction without duplicating or changing the glyph. Text-field appearances use the same encoding, including one-cell handling for variation sequences in comb fields.
- Added image XObjects with bounded 8-bit JPEG frame, component, scan, and termination inspection plus lossless DCT passthrough for grayscale, RGB, and CMYK images; deterministic Flate compression for raw grayscale, grayscale-alpha, RGB, RGBA, CMYK, and CMYK-alpha pixels; overflow-safe exact pixel-length validation; reusable resources; mirroring; and alpha soft masks.
- Added authored page thumbnails with JPEG, grayscale, RGB, RGBA, or CMYK image reuse, including shared indirect objects and alpha soft masks when the same artwork also appears on a page.
- Added Unicode metadata in both the information dictionary and XMP, document language, timezone-preserving dates, and stable content-derived trailer identifiers.
- Added new-document page layouts, initial navigation modes, reading direction, window chrome preferences, document-title display, print scaling, duplex selection, and PDF-size paper-tray selection, while retaining mandatory PDF/UA title display.
- Added typed page transitions for replace, split, blinds, box, wipe, dissolve, glitter, fly, push, cover, uncover, and fade effects, plus automatic-presentation page durations and strict direction, motion, scale, and timing validation.
- Added tagged PDF and PDF/UA-2 authoring with semantic marked content, artifact sequences, nested standard structure elements, PDF 2.0 namespaces, alternate descriptions, replacement text, page structure-parent keys, deterministic parent-tree mappings across multiple pages, accessibility identification metadata, and guarded conformance checks that reject unsupported or untagged content instead of making a false compliance claim.
- Added safe external HTTP, HTTPS, and email links plus direct internal page links, with validated rectangles and rejection of executable or local-file actions.
- Added link appearances with solid, dashed, beveled, inset, and underline borders; configurable widths, dash arrays, and horizontal or vertical corner radii; RGB colors; and none, invert, outline, or push activation highlights across external, page, and named links.
- Added precise destination views to direct page links and multi-run or rotated `/QuadPoints` hit geometry for URI, direct-page, and named-destination links, with tight union rectangles and shared geometry validation. Links now also carry stable names, page back-references, Unicode descriptions, shared author/subject/date metadata, and typed annotation flags.
- Added Unicode outline authoring with internal destinations, arbitrary nested bookmark levels, linked parent/child/sibling relationships, accurate visible-descendant counts, and automatic catalog outline mode.
- Added bookmark presentation and viewer state with bold and italic styles, RGB title colors, precise page destinations using every supported view mode, shared Unicode named-destination targets for bookmarks and document open actions, explicit open or collapsed branches, signed branch counts, and root counts that reflect only currently visible outline items.
- Added embedded files with strictly encoded portable file names, structurally validated two-token MIME types, descriptions, dates, associated-file relationships, catalog registration, and a sorted embedded-files name tree. File-name safety is platform-independent rather than relying on host-specific path rules.
- Complete document merges now resolve embedded-file registration-name collisions with deterministic suffixes while preserving each imported file specification, payload, and associated-file relationship.
- Selected-page imports now preserve page-local file-attachment annotations without copying the source catalog's global embedded-file or associated-file registrations, and omitted pages no longer bring along unrelated attachment payloads.
- Ordinary pages can now be selected from AcroForm documents without transplanting unrelated document-level form state; selected pages containing widgets still require a complete import so their field hierarchy cannot be orphaned.
- Selected-page imports now leave source-global bookmark trees behind while preserving the destination bookmark tree; complete-document imports continue to merge and remap bookmark hierarchies.
- Selected-page imports now leave source-global additional name-tree categories, such as JavaScript registrations, behind while preserving the destination categories; complete imports retain deterministic merging and duplicate-key rejection.
- Selected-page imports now preserve only destination developer-extension namespaces; complete imports still merge distinct source namespaces and reject collisions.
- Untagged pages can now be selected from otherwise tagged documents without copying the source structure tree or marking state, while pages carrying `/StructParents` still require complete tagged-document import.
- Selected pages from optional-content documents now receive bounded dependency analysis across resources, object graphs, form and pattern streams, and content tokens, with inline-image payloads excluded from lexical matching. Genuinely unlayered pages import without source `/OCProperties`; pages using OCG or OCMD state still require complete layered-document import.
- Added a selected-page corpus gate that imports and reopens the first page of every openable input while separating intentional document-level dependency boundaries from source syntax damage and output failures.
- Selected tagged pages can now be imported individually or as reordered subsets. The source structure tree, ParentTree, IDTree, page references, and document element hierarchy are pruned and remapped through a non-mutating source overlay; subsets merge into tagged destinations or combine in empty destinations, preserve PDF/UA conformance context, and remain isolated across distinct AES-256 keys. Tagged content is still rejected beside existing or newly added untagged pages.
- Selected form pages now import only widget fields reachable from the selected annotations, retaining hierarchical ancestors while pruning `/Kids`, calculation order, omitted fields, and unrelated form extensions. Pruned forms merge with destination forms using existing field-name and resource-collision guards, preserve deterministic legacy `/ProcSet` unions, and work across distinct AES-256 keys without exposing field values.
- Selected layered pages now collect OCG dependencies from their bounded page graphs, prune unreferenced groups from `/OCProperties`, recursively filter default and alternate configuration structures, preserve selected visibility, and merge multiple layered sources or existing destination configurations. Nested form dependencies and distinct AES-256 keys are supported without copying omitted layer names.
- Complete document merges now preserve additional catalog name-tree categories and their reachable object graphs, while partial imports and duplicate cross-document keys fail explicitly when generic rename semantics would be unsafe.
- Complete document merges now combine distinct catalog developer-extension namespaces and their indirect graphs; partial imports and conflicting namespace definitions fail explicitly.
- Sole complete AcroForm transplants preserve XFA packets and their catalog `/NeedsRendering` state when the destination has no form; combining XFA with another form remains fail-closed.
- Sole complete-document imports into an empty destination now preserve the source catalog's remaining standard and extension-defined entries, remap catalog back-references and page targets, and discard `/Perms` signature state that cannot remain valid after reconstruction.
- Incremental updates can explicitly replace or remove the trailer document-information dictionary. Sole complete-document transplants use this to keep source `/Info` metadata aligned with the imported XMP and catalog metadata while retaining the target file's permanent identifier.
- Incremental object freeing removes inherited trailer `/Info` only when the freed active generation is the exact registered information object; stale registrations are not silently rewritten merely because their object number was reused.
- Tagged editing now composes removal of existing tagged pages with complete tagged-document merges in one incremental revision, including direct or indirect parent trees, pruned structure children, remapped parent keys, and updated next-key state.
- Multiple transformations can compose replacements for the same existing indirect object in one incremental update; the final replacement is emitted once with the original generation.
- Added AcroForm text and comb fields, checkboxes, multi-page radio groups, and editable or fixed combo boxes with stable names, values, widget links, appearances, resources, and matching on/off states. Checkbox and radio export values cannot collide with the reserved `/Off` appearance state.
- Added Unicode AcroForm tooltips and mapping names for accessible field descriptions and stable export mappings, plus read-only, required, and no-export behavior across text fields, checkboxes, radio groups, and combo boxes.
- Added single-select AcroForm list boxes with validated options and selections, Unicode font support, visible row appearances and selection highlighting, accessibility metadata, and shared field behavior.
- Added multi-select AcroForm list boxes with option-ordered value arrays, matching selection indices, multiple highlighted appearance rows, and validation against duplicate or unknown selections.
- Added validated list-box top indices with matching `/TI` serialization and appearance viewports for long option lists.
- Added AcroForm push buttons with safe HTTP, HTTPS, or email URI actions, Latin-1 or embedded Unicode labels, generated appearances, accessibility metadata, and shared field behavior.
- Added internal-page push buttons using the shared precise destination serializer for fit, coordinate, rectangle, and bounded-zoom views.
- Added push buttons targeting shared Unicode named destinations, with definition-order validation and compact GoTo actions.
- Added reset-form push buttons for all fields or validated named subsets, including PDF exclusion semantics for resetting every field except the listed set.
- Added default values matching authored initial values across text, checkbox, radio, combo, and list fields so reset actions restore deterministic initial state.
- Added independent text-field default values so reset actions can restore a value other than the initially displayed one, with matching Unicode, line-break, and maximum-length validation.
- Added independent checkbox and radio-group default states while preserving current widget appearance states, enabling reset actions to restore intentionally different selections.
- Added independent combo-box and list-box default selections, including option-ordered multi-select defaults and export-value validation, so resets can restore a different choice state.
- Added typed file-selection text fields with the PDF file-select flag and validation against incompatible multiline, password, or comb behavior.
- Added XHTML rich-text values for text fields with secure XML parsing, required XHTML body roots, the matching rich-text field flag, and rejection of password, file-selection, or comb combinations.
- Added reusable text-field visual styles with optional backgrounds and borders, RGB text colors, finite nonnegative border widths, matching widget appearance characteristics, and generated appearances for single-line, multiline, password, and comb fields.
- Extended reusable visual styles to combo boxes and list boxes, including default appearances, widget background and border characteristics, and styled generated text and selection-list appearances.
- Extended reusable visual styles to every push-button action and visible unsigned-signature prompt, keeping action highlighting and embedded-font behavior while synchronizing widget characteristics and generated appearances.
- Extended reusable visual styles to checkbox and radio-button widgets, applying configurable backgrounds, borders, widths, and mark colors consistently to widget characteristics and every on/off appearance state.
- Added typed rollover and pressed captions to every push-button action, with Unicode font validation, matching widget characteristics, and independently generated normal, rollover, and down appearance streams.
- Added measured left, center, or right push-button caption alignment across normal, rollover, and down appearances, using actual embedded-font advances when available.
- Added all five standard widget border styles with matching solid, validated dashed, beveled, inset, or underline `/BS` dictionaries and generated appearances across rectangular and radio-button widgets.
- Added reusable RGB or RGBA push-button icons with all seven standard caption positions, proportional or independent scaling, always/never/too-large/too-small scale policies, normalized alignment anchors, fit-to-bounds behavior, matching icon-fit dictionaries, and generated icon artwork across normal, rollover, and down states.
- Added independent rollover and pressed push-button icons with `/RI` and `/IX` widget characteristics, shared image deduplication, and state appearances generated even when only the icon changes.
- Added measured left, center, or right visible signature-prompt alignment plus typed optional or required `Adobe.PPKLite` signing-handler seed constraints with the correct filter flag.
- Added typed PDF 1.5, PDF 1.7, or PDF 2.0 signature seed-parser capability constraints, written as the specification-required real number with an independently enforceable required flag.
- Added signature seed constraints for RFC 3161 timestamp servers and legal attestations, including independent required timestamp and attestation controls with strict URL and list validation.
- Added complete typed X.509 signing-certificate seed constraints for acceptable signer and issuer certificates, certificate-policy object identifiers, subject distinguished names, all nine standard key-usage bits, and credential-enrollment or signature-service URLs, with independent required enforcement and strict validation.
- Added PDF 2.0 document-change permissions to signature field locks, covering no changes, continued form filling and signing, or annotation changes, and corrected both field-lock and seed-value dictionaries to use their required indirect-object form.
- Completed standard PDF 2.0 signature seed-value authoring with typed automatic, required-lock, or required-unlocked document intent and named signing-appearance constraints, including their independent required flags and validation.
- Detached approval signing now fills an existing named unsigned signature field in place, preserving its widget appearance, geometry, field lock, seed constraints, hierarchy, and annotation placement while adding only the signature value and required signature flags.
- Detached approval signing now analyzes existing DocMDP certification permissions, permits signing through pre-authored fields at permission levels 2 and 3, rejects no-change certification, refuses to add fields to certified documents, and fails closed on malformed or ambiguous certification transforms.
- Added detached certification signatures with all three DocMDP permission levels, catalog permission registration, direct standard transform dictionaries, first-signature enforcement, and safe composition with newly created or existing signature fields.
- Signing a field with an authored lock now binds its action, field list, and document-change permission into the signature through a standard FieldMDP transform, so the preserved lock participates in signature validation instead of remaining descriptive metadata.
- Detached signing now enforces required seed-value constraints for the signing handler, parser capability, encoding, digest declaration, reason, legal attestation, revocation declaration, approval or certification mode, document-lock intent, named appearance, RFC 3161 timestamp token, and timestamp server.
- Signature inspection now requires the byte-range gap to be exactly the literal or hexadecimal `/Contents` string. A range that excludes any additional document byte is reported as structurally invalid before CMS verification.
- Certification inspection now matches the complete `/Perms /DocMDP` object number and generation and resolves that target before trusting it, so stale certification references fail closed.
- Signature discovery, field-name validation, existing-field lookup, and signed-field detection now use complete indirect identities for AcroForm traversal, preventing stale generations from being misreported as cycles or duplicate fields.
- DocMDP and FieldMDP inspection now validates optional transform-parameter `/Type` and `/V` entries when present, honoring the standard `/1.2` default while rejecting contradictory explicit values.
- Added signer-certificate enforcement for exact acceptable certificates, linked issuer evidence, certificate-policy OIDs, subject distinguished names, all nine key-usage constraints, and acquisition URLs. Required signer certificates must also be embedded in the returned CMS and selected by its signer identifier.
- Added structural signature inspection for unsigned and signed AcroForm fields, including hierarchical field names, approval or certification identity, filter and encoding names, bounded byte-range validation, whole-document coverage, raw placeholder contents, padding-free bounded CMS values, and exact reconstruction of the bytes supplied to detached-signature verification.
- Added detached CMS cryptographic verification with separate mathematical-integrity and certificate-chain-trust results, using the platform PKCS implementation and reconstructed signed bytes without conflating an untrusted signer certificate with a broken signature.
- Added signed-revision analysis that reopens the exact signed PDF prefix, counts later incremental revisions, and identifies indirect objects added, updated, or freed after the signature, including hybrid cross-reference entries.
- Added conservative DocMDP assessment to signed-revision analysis: unchanged certification signatures are reported cleanly, later changes under no-change certification are reported as prohibited, and permission levels requiring object-level semantic interpretation are explicitly sent for review rather than falsely marked valid.
- Detached signing now rewrites direct signature-field dictionaries at the AcroForm root or inside nested field trees while preserving indirect ancestors, and it signs pre-authored fields in tagged PDFs without changing their structure trees.
- Creating a signature field while signing a tagged PDF now adds a page-linked `/Form` structure element, annotation object reference, parent-tree mapping, widget structure-parent key, and accessible alternate description. Direct structure roots are safely indirected with repaired top-level parents.
- Bookmark-tree merging now accepts direct destination outline roots by indirecting the root and repairing existing top-level parent and sibling links before imported bookmark segments are attached.
- Added optional or required signature revocation-information inclusion constraints with validated required-state semantics and the matching seed-value flag.
- Added full-PDF submit buttons restricted to HTTP or HTTPS endpoints, with URL file specifications and validated include or exclude field lists.
- Added typed push-button highlighting for none, invert, outline, push, and toggle interaction modes.
- Added typed checkbox marks with matching appearance characteristics and generated check, cross, circle, diamond, square, or star artwork.
- Added clipped multiline text-field appearances with normalized line endings, explicit baselines and leading, plus rejection of line breaks in single-line fields.
- Added masked password-field appearances that never paint the original value, with embedded-font mask-glyph validation and rejection of incompatible multiline password fields.
- Added comb-field appearances with evenly divided cells and independently positioned glyphs matching the declared maximum length.
- Added left, centered, and right text-field alignment with matching `/Q` values and measured appearance positioning for embedded fonts.
- Added width-aware multiline wrapping with preserved paragraph breaks and hard wrapping for individual words wider than the field.
- Added initial-value fit validation for no-scroll single-line and multiline fields so authored content is not inaccessible from the outset.
- Added combo-box and list-box options with separate export and display values, preserving compact scalar options when both values are identical.
- Added left, centered, and right choice-field alignment with matching `/Q` values and measured combo or list appearance positioning.
- Added typed signature-field locks for all fields or validated include and exclude subsets, ready to take effect when the field is signed.
- Added typed signature seed values for detached PKCS#7 or CAdES encodings, validated SHA-256, SHA-384, or SHA-512 digest constraints, permitted signing reasons, and approval or certification-signature permissions, with independently enforceable required flags.
- Added unsigned digital-signature fields with page-linked widgets, document signature flags, accessibility and mapping metadata, shared field behavior, and collision-safe names.
- Added optional visible unsigned-signature prompts with generated border and text appearances, Latin-1 or embedded Unicode fonts, and PDF/A-safe embedded-font enforcement.
- Added typed radio-group behavior for preventing toggle-to-off and selecting identically named controls in unison, serialized alongside common field flags.
- Added typed choice-field behavior for sorted option arrays, spell-check suppression, and immediate commit on selection changes across combo and list fields.
- Added typed text-field behavior for spell-check suppression and preventing scrolling beyond the visible field bounds.
- Added embedded Unicode TrueType fonts to text-field and combo-box values and appearances, sharing deterministic subsets through the AcroForm default resources.
- Added bounded Gray, RGB, and CMYK ICC profile loading with mandatory tag-table presence, unique tag signatures, aligned in-range tag data, and trailing-byte trimming, plus PDF/A-4, PDF/A-4e, and PDF/A-4f authoring with required metadata, output intents, flavour-specific identification schemas, and associated embedded files.
- Added editable PDF 2.0 text notes with all seven standard icon names, marked or review workflow states, named direct or grouped reply relationships, and linked popup windows; highlights, underlines, strikeouts, and squiggles over multiple axis-aligned or rotated text runs; left-, center-, or right-aligned multiline free-text boxes with solid or dashed borders; solid or dashed lines and polylines with all ten standard PDF line-ending styles and optional interior colors; solid or dashed polygons, rectangles and ellipses; solid or dashed multi-stroke ink; and image stamps with all standard semantic stamp identities, Unicode contents, optional author, subject, creation and modification metadata, typed print and interaction flags, and deterministic appearances.
- Added standard intent semantics for ordinary, callout, and typewriter free text; arrow and dimension lines; polyline and polygon dimensions; and polygon clouds. Free-text callouts include validated two- or three-point geometry, standard line endings, expanded annotation bounds, and matching explicit appearances.
- Added visible file-attachment annotations with graph, paperclip, push-pin, and tag identities, page-linked appearances, shared embedded-file specifications, Unicode contents, and annotation metadata, including PDF/A-4f associated-file authoring.
- Added editorial caret annotations with optional paragraph symbols and scalable vector appearances, plus multi-run and rotated redaction marks with explicit review appearances, distinct post-redaction fill colors, and optional aligned or repeated replacement text. Redaction marks identify content for later removal and do not falsely report that page content has already been sanitized.
- Added image-stamp annotations for pictures and scanned signatures. JPEG payloads remain untouched, repeated stamps share image resources, and RGBA stamps preserve transparency through shared soft masks.
- Added deterministic incremental updates that append without changing any source byte, preserve generations and trailer inheritance, retain permanent identifiers, advance revision identifiers, and work across classic, hybrid, cross-reference-stream, and compressed-object sources.
- Incremental updates can now explicitly emit sparse PDF 1.5+ cross-reference streams with optional deterministic Flate compression, correct `/Prev` chaining, and encryption exemption, while classic tables remain the default.
- Added an incremental cross-reference-stream smoke generator whose compressed output passes qpdf structural validation.
- Incremental cross-reference-stream revisions can now pack eligible generation-zero updates into deterministic object streams bounded at 100 objects each, with optional Flate compression and authenticated encryption of the containing stream. qpdf recognizes and validates the emitted compressed entries.
- Existing generation-zero objects can be superseded by deterministic compressed entries in an incremental revision, including catalog replacements.
- Incremental structural output now honors valid catalog `/Version` overrides when checking whether PDF 1.5 cross-reference and object streams are permitted.
- Incremental structural output uses a pending catalog `/Version` override only when the replacement matches the trailer `/Root` object number and generation; a stale root cannot borrow an unrelated active replacement to authorize newer syntax.
- Full cross-reference-stream rewrites now apply the same effective-version rule, allowing a PDF 1.4 header with a valid catalog `/Version /1.5` override to retain its header while using PDF 1.5 structures.
- Incremental updates can now free existing direct or compressed objects with advanced generations and complete object-0 free-chain updates in classic tables or cross-reference streams. Freeing and replacing the same object composes to the final requested action, and a two-revision smoke output passes qpdf.
- Prior compressed-object entries can be freed directly and are superseded by generation-one free entries without resolving the obsolete packed value.
- Incremental freeing now protects the catalog and encryption dictionary from becoming dangling trailer roots and automatically removes inherited `/Info` when its backing object is freed, while final-action composition still permits free-then-replace workflows.
- Newly freed objects now link to the inherited free-list head, and canonical free actions contribute to revision-identifier derivation so distinct free-only revisions receive distinct updated `/ID` values.
- Incremental page and annotation editors now accept structural write options and can emit compressed cross-reference and object-stream revisions. The compressed annotation-editor smoke output passes qpdf.
- Compressed page editing works across independently encrypted AES-256 source and destination documents, and a dedicated AES-256 compressed incremental smoke output authenticates and passes qpdf without syntax or stream errors.
- Detached signatures now accept compressed cross-reference and object-stream policies while keeping only the patchable signature dictionary direct. A real certification CMS revision contains compressed field updates and passes OpenSSL, KillerPDF verification, and qpdf.
- AES-256 encrypted documents can receive cryptographically verified signatures with compressed cross-reference and object streams while retaining destination-password authentication and a direct signature dictionary.
- Signed-revision analysis now has explicit coverage for added compressed objects, updated objects, and freed objects in later structural revisions.
- Signed-revision analysis now reports malformed filtered cross-reference data, unsupported historical syntax, and numeric overflow in a signed prefix as an invalid historical revision instead of throwing.
- AES-256 encrypted annotation editing can emit compressed object streams, reopen annotation text with the destination password, and keep that text out of clear file bytes.
- Incremental page and annotation editors now enforce catalog DocMDP certification permissions before writing. Page-tree changes are rejected under every certification level, while annotation changes require permission level 3; malformed certification structures fail closed.
- Full rewrites now reject documents containing signed signature fields by default because rewriting necessarily invalidates their byte ranges. Callers performing deliberate archival or forensic rewrites must explicitly opt in to signature invalidation.
- Added a source-preserving incremental corpus mode. It appends, reopens, and resolves a marker across 2,235 of 2,236 normalized fixtures; only the intentionally undefined PDF 1.9 header is rejected.
- Added password-authenticated Standard Security reading for revisions 2 through 6, including user and owner passwords, RC4, AES-128, AES-256, independent string, stream, and embedded-file crypt filters, cleartext metadata, encrypted object streams, and unencrypted cross-reference streams.
- Encrypted documents can now receive incremental updates, full rewrites, and detached signatures without exposing new strings or streams. Encryption dictionaries and permanent identifiers are preserved, signature contents remain verifiable, and outputs interoperate with qpdf across every supported security revision.
- New PDF 2.0 documents can now be authored with AES-256 revision 6 password protection, distinct user and owner credentials, encrypted strings, streams, metadata, and embedded files, authenticated typed controls for printing, modification, copying, annotations, form filling, accessibility extraction, and assembly, and explicit rejection of encryption combined with PDF/A conformance.
- Revision 5 and 6 password encoding now rejects unpaired UTF-16 surrogates instead of silently replacing them during UTF-8 conversion, preventing invalid passwords from collapsing to identical authentication bytes.
- Revision 6 password preparation now removes every RFC 3454 table B.1 character before compatibility normalization. User and owner passwords containing soft hyphens or BOMs authenticate through their mapped forms in KillerPDF and qpdf.
- Revision 6 password preparation now rejects SASLprep-prohibited controls, formatting and direction controls, private-use scalars, noncharacters, inappropriate plain-text and canonical characters, and tagging characters.
- Revision 6 password preparation now enforces generated Unicode 3.2 A.1, D.1, and D.2 tables, including unassigned-code-point rejection and complete RandALCat/LCat endpoint and mixing rules. Non-ASCII spaces and compatibility characters map to qpdf-compatible forms.
- AES-256 authentication now validates the permission block's required reserved bytes. Revisions 2 through 6 reject a non-boolean `/EncryptMetadata` value rather than silently treating malformed state as false, and named crypt filters validate method-specific lengths and authentication-event values.
- Standard Security crypt-filter selectors now honor the specified `/Identity` default when `/StmF` or `/StrF` is omitted, and crypt-filter dictionaries honor the specified `/None` method default when `/CFM` is omitted. Explicit malformed selector and method values remain rejected.
- Password-authenticated documents now expose whether the user or owner password succeeded plus a typed view of every declared operation permission. High-level page, annotation, signing, import, and full-rewrite operations enforce user-password assembly, rotation, page-box, annotation, form-filling, content-copying, and general-modification restrictions, while owner authentication retains unrestricted access and the low-level incremental builder remains available for deliberate forensic revisions. Legacy authentication prefers a valid owner path so an identical owner and user password is not downgraded to user privileges.
- Typed encryption options reject the contradictory combination of high-quality printing with printing disabled, and authenticated permission views only grant high-quality printing when the base printing permission is also present.
- Standard Security revisions 2 through 6 now validate every required zero and one bit reserved by `/P`, preventing malformed permission words from being accepted or exposed as authorization state.
- Legacy Standard security now rejects non-byte-aligned 40-bit through 128-bit key lengths instead of truncating malformed bit counts during byte conversion.
- Legacy Standard security now honors the specified 40-bit default when the optional global `/Length` entry is omitted.
- Revision 2 through 4 passwords now use complete, strict PDFDocEncoding rather than lossy Latin-1 replacement. Euro-sign user and owner passwords authenticate against an independently generated qpdf AES-128 fixture, and unrepresentable Unicode fails explicitly.
- Legacy Standard-security dictionaries now require a defined `V=1/R=2`, `V=2/R=3`, or `V=4/R=4` algorithm and revision pair.
- Structural inspection now distinguishes missing or incorrect encryption credentials from document damage: unauthenticated files request authentication without requiring repair, while password-aware inspection resolves encrypted compressed objects and reports incorrect credentials without throwing.
- Authenticated structural inspection now reports corrupted AES ciphertext in an indirect stream as object damage rather than throwing or misclassifying it as a credential failure.
- Password rejection now has a dedicated internal failure type, allowing inspection to distinguish incorrect credentials from authenticated encryption-integrity damage such as a corrupted AES-256 permission block.
- Authenticated round-trip validation now verifies two clean rewrites with equivalent decrypted logical object graphs while preserving fresh randomized AES IVs instead of requiring byte-identical ciphertext, and reports incorrect credentials without throwing.
- Explicit stream crypt-filter selection now rejects malformed `/Filter` and `/DecodeParms` arrays, and `/EFOpen` authentication events cannot be assigned to `/StrF` or `/StmF` document-open filters.
- Added explicit stream `/Crypt` filter support with required first-filter ordering, `/Identity` cleartext selection, named RC4 or AES crypt-filter selection, decoder integration, and faithful incremental and full-rewrite preservation.
- Authenticated pages from password-protected PDFs can now be imported into plain or encrypted destinations, with decrypted source objects re-encrypted under the destination security handler while unauthenticated sources remain fail-closed.
- Existing optional-content documents now permit safe page removal, addition of unlayered pages, and complete layered-document merges. OCG arrays, default visibility, display order, locked groups, radio groups, usage applications, alternate configurations, and imported page-property references are combined while ambiguous `/Unchanged` source states remain fail-closed.
- Existing tagged documents now permit complete page reordering, insertion of truly blank pages, and page removal. Removed-page marked-content and object-reference items are pruned recursively, affected structure elements retain their object identities, and direct or indirect ParentTrees are rebuilt without removed page keys.
- Complete tagged documents can now be merged into an existing tagged destination with collision-free structure-parent keys and IDs, combined ParentTrees, IDTrees, namespaces, associated files, and pronunciation lexicons, one retained PDF/UA `Document` root, repaired top-level parents, direct-root normalization, and collision-safe RoleMap and ClassMap renaming. Unknown source root extensions remain fail-closed instead of being silently discarded.
- Complete tagged-document merges now preserve distinct extension-defined structure-root entries and their reachable object graphs. Colliding extension keys and selected-page imports with unknown extension dependencies remain fail-closed.
- Selected tagged-page imports no longer copy source-global developer-extension namespaces through the PDF/UA conformance-property path. Complete imports still preserve and merge distinct namespaces.
- Tagged complete-document merges seed the shared structure root and top-level `Document` identities before importing catalog and structure-root extension graphs, so extension-defined back-references resolve to the merged objects instead of cloning or double-mapping the source structure tree.
- Incremental annotation editing now normalizes a direct catalog structure-tree root through the unambiguous indirect parent referenced by its top-level elements. A direct top-level `Document` element is promoted in place with a repaired parent link, preserving tagged/PDF/UA structure instead of rejecting or misattaching the update. Ambiguous direct roots remain fail-closed.
- Tagged incremental annotation updates resolve indirect structure-root `/Namespaces` arrays, indirect root-kids arrays, and indirect `Document`-kids arrays. Namespace selection still chooses the PDF 2.0 standard namespace, and appended annotation elements remain flat siblings instead of nesting an array reference as a child.
- Incremental annotation normalization reuses the indirect top-level `Document` identity referenced by existing children, so old and appended structure elements share one parent and IDTree or extension references cannot remain attached to a duplicate. Ambiguous child parent identities fail closed.
- PDF/UA-2 authoring rejects the generic `H` structure type as required by the UA-2 validation profile and directs callers to `H1` through `H6`; ordinary tagged-PDF authoring continues to support generic headings.
- Tagged document merging and incremental annotation editing use checked structure-parent key allocation. An exhausted `long` key space now fails before writing instead of wrapping `/ParentTreeNextKey` and new `/StructParent` values negative.
- Structure-specific ParentTree handling rejects negative existing keys and negative declared `/ParentTreeNextKey` values in tagged merge and incremental annotation paths, while the generic number-tree reader remains available for signed-key trees outside structure semantics.
- Complete tagged-document merges and selected-page structure pruning flatten indirect structure-root and top-level `Document` kids arrays, retaining one `Document` root and a valid sibling sequence instead of nesting array objects as structure children. Reused indirect kids arrays remain malformed shared structure and fail closed.
- PDF/UA authoring assigns page and annotation ParentTree keys through one checked monotonic allocator across links, widgets, notes, markup, editorial annotations, and attachments. `/ParentTreeNextKey` is taken from the allocator's final state rather than recomputed through unchecked count sums.
- Tagged complete-document merging promotes a direct destination `Document` by reusing the unambiguous indirect identity referenced by its children, retaining child parent links, IDTree targets, and extension references on one object. Direct source `Document` elements use the same identity rule; ambiguous child parent references remain fail-closed.
- AcroForm merging now preserves distinct catalog-level extension entries and their imported object graphs, reports extension-key collisions explicitly, and keeps XFA merges fail-closed because independent template and dataset packets cannot be combined safely without XFA-specific semantics.
- Added bounded ASCIIHex, ASCII85, RunLength, and LZW stream decoding with abbreviations, chained pipelines, malformed-data rejection, LZW table resets and PDF `EarlyChange`, and predictor processing after each decoded stage. Safety limits also cover unfiltered data and Crypt pass-through stages. TIFF and PNG predictors support all standard 1, 2, 4, 8, and 16-bit component depths; fixed PNG predictors enforce their declared row filter while predictor 15 permits mixed optimum filters.
- Added byte-preserving PDF 2.0 approval signing with invisible AcroForm signature fields, exact fixed-width byte ranges, bounded detached CMS placeholders, `ETSI.CAdES.detached` signature dictionaries, Unicode signer details, existing-form preservation, and a dependency-free callback boundary for software certificates, cloud keys, and hardware tokens.
- Added byte-preserving annotation editing for existing PDFs, including shared embedded-font free-text resources and direct or indirect annotation arrays in nested page trees.
- Added byte-preserving blank-page insertion, rotation, reordering, deletion, resizing, and cropping. Retained page identities, contents, inherited resources, boxes, and rotations remain intact when page trees are rebuilt.
- Added cross-document page import for merge and split workflows with deterministic reference remapping for encoded streams, content, fonts, images, resources, ordinary annotations, direct links among imported pages, and independent repeated copies of the same source page.
- Added document metadata, language, viewer-preference, and output-intent preservation when one complete source is transferred into an empty destination, retaining its archival and accessibility declarations instead of silently dropping them.
- Added complete tagged-PDF structure transfer into empty destinations, preserving the structure tree and parent mappings. Partial or combined tagged imports and page-set changes that would leave stale structure data now fail explicitly, while complete tagged page sets can still be reordered safely.
- Added complete AcroForm preservation when all pages of a form document are imported, including merges into destinations that already contain forms and merges from multiple form documents. Field arrays, hierarchical field identities, signature flags, calculation order, default quadding, and default resources are reconciled deterministically; imported default appearances receive collision-free resource names even when the source uses escaped PDF names, while true duplicate field names and unsafe partial imports fail explicitly.
- Added named destinations, stable named links, and page-label ranges with decimal, Roman-numeral, alphabetic, prefix, and custom starting-number options.
- Added initial document views and rich named destinations with explicit coordinates, zoom, page, width, height, bounding-box, and rectangular fitting modes.
- Added modern name-tree and legacy catalog named-destination preservation during document imports while retaining the destination document's other name-tree categories. Complete imports retain every destination, colliding modern and legacy names are deterministically renamed together with their links, and split workflows retain navigation whose targets remain inside the selected pages while references outside the split fail explicitly.
- Added page-label preservation across insertion, deletion, reordering, merge, and split operations, keeping each retained page's effective prefix, numbering style, and number while rebuilding compact ranges for the new page order.
- Added bookmark-tree, embedded-file, and associated-file preservation during complete document imports, including bookmark-tree merging across the destination and multiple sources, repaired parent and sibling links, remapped targets, merged attachment name trees, retained outline display mode, and explicit collision or unsafe-partial-import failures.
- Added byte-accurate tokenization and a typed object model for numbers, names, strings, arrays, dictionaries, indirect references, object records, and binary-safe stream payloads. Source-aware errors enforce nesting, numeric, truncation, and stream-boundary rules, while stream openings accept all three PDF line endings without consuming payload bytes.
- Added bounded Flate/zlib decoding with 8-bit TIFF and PNG predictor reversal for compressed cross-reference streams and object streams without unbounded decompression.
- Added final `startxref` discovery plus classic, hybrid, and cross-reference-stream parsing. Incremental `/Prev` revisions merge newest-first, inherited trailer values remain available, and malformed offsets and cycles are rejected.
- Added a lazy document loader for ordinary and compressed objects, indirect stream lengths, generation validation, object-stream boundaries, decoding caches, and resolution-cycle rejection.
- Added bounded structural inspection reports that retain byte offsets and object numbers while distinguishing header, cross-reference, indirect-object, and catalog failures for repair decisions.
- Structural inspection now converts invalid trailer offsets and other structural argument or state failures into repair diagnostics instead of allowing them to escape.
- Added deterministic serialization for every object-model type, including invariant numbers, decoded-byte dictionary ordering, canonical escaping, stable LF output, exact stream lengths, and nesting limits.
- Added deterministic full-file rewriting from the merged document view. Rewrites expand compressed objects, remove obsolete cross-reference containers, sanitize the trailer, preserve requested metadata and identifiers, and preserve authenticated document encryption.
- Added an explicit deterministic cross-reference-stream rewrite format for PDF 1.5 and later, including the stream's own in-use entry, encryption exemption, reopen validation, and byte-stable repeated output.
- Added optional deterministic object-stream packing during cross-reference-stream rewrites. Eligible generation-zero objects receive compressed cross-reference entries, stream objects and encryption dictionaries remain direct, and encrypted output reopens without exposing packed object data.
- Object-stream packing is split into deterministic groups of at most 100 objects, keeping decoder memory bounded and compressed-object lookup granular on very large documents.
- Added optional deterministic Flate compression for emitted cross-reference and object streams, including encrypted object-stream rewrites and byte-stable repeated output.
- Full rewrites now remove obsolete linearization parameter dictionaries together with prior cross-reference and object-stream containers, preventing stale fast-web-view metadata from surviving non-linearized output.
- Classic and stream-based full rewrites now emit complete linked free-object chains across object-number gaps instead of leaving sparse entries unspecified.
- Full rewrites now preserve inherited free-object generations and retired object-number high-water marks in classic tables and cross-reference streams, preventing later incremental updates from reviving permanently retired objects while retaining byte-stable structural rewrites.
- Full rewrites now also retain sparse object-number reservations expressed only through trailer `/Size`, so later incremental updates cannot reuse implicitly reserved numbers.
- Removing document information during a full rewrite now physically omits a dedicated indirect `/Info` object and its text, retires its object number with an advanced generation, and fails closed if another object shares that reference.
- Full rewrites can now remove both standard document-level metadata stores, physically deleting trailer `/Info` and dedicated catalog XMP stream objects in classic-table or compressed-stream output. AES-256 protection and password authentication remain intact, while shared XMP objects fail closed instead of leaving dangling references or private bytes behind.
- Full-rewrite metadata removal matches `/Info` and catalog `/Metadata` by object number and generation, so stale registrations are removed without deleting the active object that happens to reuse their object number.
- Full and incremental writers now reject undefined metadata-policy and cross-reference-format enum values instead of allowing cast values to fall through to an unintended output policy.
- Full rewrites now retain nonstructural application trailer entries in classic and stream output while removing revision-only cross-reference fields and invalidated document checksums.
- Full rewrites require a live indirect catalog root, a live indirect document-information dictionary when preserved, valid two-string document identifiers, and identifiers for encrypted output before catalog-dependent inspection or serialization begins.
- Preserved application trailer graphs are traversed through bounded arrays, dictionaries, streams, and indirect references, rejecting stale object generations instead of serializing dangling references.
- Incremental classic-table and cross-reference-stream revisions now retain application trailer state while removing an invalidated `/DocChecksum` from the new revision.
- Incremental updates recursively validate preserved application trailer graphs against both current and pending object generations, rejecting dangling references and references to objects freed by the new revision.
- Incremental-update preflight requires a live catalog root, live inherited or replacement document information, valid two-string identifiers, and a live encryption dictionary before appending bytes.
- Full and incremental writers resolve indirect catalog type and version names while retaining strict catalog identity and defined-version checks.
- Catalog version syntax is validated for classic-table output as well as cross-reference streams, preventing malformed version overrides from surviving either write format.
- Preserved and replacement document-information dictionaries validate every standard text field, calendar and timezone syntax for PDF dates, and the defined `/Trapped` states at both full and incremental write boundaries.
- Writer document-information validation resolves valid indirect standard fields while stale or mistyped values remain fail-closed.
- Added rewrite policy for preserving or upgrading the PDF header, retaining or removing document information, and independently retaining document identifiers, plus reusable round-trip and corpus validation.
- Accepted the complete PDF 2.x header declaration range through `%PDF-2.9` and corrected classic cross-reference `/Size` handling for free boundary entries found in PDF/A-4 fixtures.
- Header parsing requires CR or LF immediately after the single-digit major and minor version, rejecting truncated declarations and longer strings that merely begin with a defined version.

### Added
- Grab cursors: an open hand over anything that can be picked up and a closed hand while it is being carried, on the annotation bars, the find bar and signatures popup, page panning, stamp placement and the Transform perspective handles.

### Fixed
- Stream parsing now accepts qpdf-compatible files whose declared stream length includes the final line ending and therefore places `endstream` immediately after the payload. Exact declared lengths and the closing keyword still bound the binary data unambiguously.
- Cross-reference parsing now caps each classic or stream section at one million entries and rejects oversized subsection counts or `/Index` ranges before iterating or decoding their rows.
- Object-stream reading now rejects containers declaring more than one million compressed objects before decoding their headers or allocating object lists.
- Page, name, and number tree readers now reject reused indirect nodes, preventing malformed shared subtrees from multiplying traversal work while preserving cycle and duplicate-key diagnostics.
- AcroForm merge inspection and tagged page-removal traversal now reject reused indirect fields or structure elements rather than revisiting malformed shared subgraphs.
- Link annotations now include the print flag required for PDF/A-4 annotation conformance.
- The About card's update button now keeps readable text on hover and uses the correct beveled button treatment in the 98SE theme.

## [1.7.5] - 2026-08-22

KillerPDF 1.7.5 is a small maintenance release that closes several visible annotation, scrolling, shortcut, theme, and localization regressions. It keeps the faster scrolling introduced in 1.7.4, makes Transform trustworthy with freshly placed text, and gives the text annotation toolbar a cleaner two-row layout.

### Added
- Shift+mouse wheel now scrolls wide pages horizontally, using the same scrolling path as a tilt wheel (#209, thanks Ryokoxx).
- Ctrl+B, Ctrl+I and Ctrl+U now bold, italicize and underline while you are editing a text box. They were listed in the shortcuts for weeks without ever being wired up.
- Hungarian OCR completes the twelve-language OCR catalog, so every language available for the KillerPDF interface now has a matching downloadable recognition model.

### Changed
- The text annotation toolbar now uses a deliberate two-row layout: font above size, text color above fill color, and text opacity above fill opacity. It is taller but substantially narrower, with each lower control aligned beneath its corresponding upper control instead of leaving Fill Opacity stranded on an accidental wrapped row.
- The sidebar moved from Ctrl+B to F9, and moving it left or right from Ctrl+Shift+B to Shift+F9. Ctrl+B was documented as bold and as the sidebar at the same time, and it was the sidebar that answered. F9 was the one function key with nothing of its own to do: the four view modes still have F5 to F8, and the wheel over the view still cycles them.
- The current-page span badge now casts a small shadow beneath its rectangle, while its text remains independently rendered and crisp. The 98SE theme keeps the badge flat with the rest of its classic chrome.

### Fixed
- Rotating a page now keeps upright text boxes, images, and signatures inside the new page bounds. Their centers still follow the rotated sheet, but an item near the old long edge is clamped before it can become invisible and unrecoverable off-page (#169, thanks terada-d).
- Fast wheel scrolling in Single Page and Two-Page views no longer carries its remaining momentum into an accidental page change at the edge. Scrolling keeps its existing speed; changing pages requires a deliberate second wheel gesture (#205, thanks 1mk3r).
- Transform now commits an active text box before building its preview, so text placed immediately before opening Transform is included in both the preview and the transformed page.
- Grid zoom now updates every page seam in one layout pass, so the pages no longer resize first and then visibly settle one border at a time as their refreshed bitmaps arrive.
- Switching themes, accents, or languages with an annotation or crop bar open now rebuilds that bar completely in both split panes. This fixes controls retaining colors from the previous theme, including the light-theme mismatch, and keeps code-built crop labels, tooltips, and buttons current without reopening the tool.
- Nine dialogs, including the install and update prompts, now preserve their intended line breaks. The strings carried the breaks but not the attribute that stops XAML collapsing them, so adding more had no effect (#231, thanks bovirus).
- Shortcut key and mouse names, including Ctrl, Shift, Home, End, Delete, Click and Scroll, are now translatable in all twelve languages. The list and visual keyboard are generated from one shared table, fixing the missing Alt+M entry and inconsistent navigation descriptions while preventing the two views from drifting again (#230, thanks bovirus).
- Shared dialog buttons now translate OK, Cancel, Yes, and No, including the custom color picker (#227, thanks Mr-Update).
- Recent files now translate the `missing` label instead of leaving it in English (#227, thanks Mr-Update).
- Annotation copy, paste, and delete confirmations now use the active language and the correct singular or plural message (#227, thanks Mr-Update).
- Search now translates its empty, error, summary, navigation, and close messages. Its result field is wider so longer translated states are not clipped (#227, thanks Mr-Update).
- OCR model downloads now translate their progress and cancellation hints, including multi-model downloads, and flatten/export progress is translated as well (#227, thanks Mr-Update).
- The portable launcher now publishes cleanly with the .NET 10 SDK without trying to copy an unused binding-redirect configuration file.

## [1.7.4] - 2026-08-21

KillerPDF 1.7.4 keeps the convenience of one portable download while installing as a normal multi-file application, cutting initial startup time substantially. This release also fixes annotation rotation, form fields on comma-decimal locales, installation scope, and a range of viewer, dialog, and localization problems. Hungarian localization and page image export are included as well.

### Added
- "Export page as image" on the Pages panel's right-click menu, including multi-page selections (#207, thanks 1mk3r).
- Hungarian (hu-HU) localization, the twelfth interface language, in the language picker as "Magyar" (PR #214, thanks CsokiHUN).
- Hide the toolbar from its right-click menu or Alt+M, and full screen no longer sits over other applications when you switch away (#215, thanks Subjuntivos).
- Translations can be tested in a normal install and reload on every save of the file; TRANSLATING.md has the steps (#211, thanks bovirus).
- The page badge fires on grid scrolling and names the visible span (#197, thanks Ryokoxx).

### Changed
- KillerPDF now remains one portable download while installing as a normal multi-file application. The portable EXE carries one compressed, verified payload and cleans up its temporary files after use; installed shortcuts launch the inner app directly, avoiding Costura extraction and reducing measured first startup by about 40% on the development machine (#189, thanks ags1234). The new package is also roughly 34% smaller than the previous woven EXE.
- Builds no longer risk the net48 CS8336 attribute collision introduced by compiler-generated polyfills (PR #218, thanks Ryokoxx).

### Fixed
- Printing now composes and spools on a dedicated thread, keeping the progress window responsive throughout large jobs (PR #228, thanks Ryokoxx). Print layout choices are frozen when the job begins so keyboard input during preparation cannot change N-up grouping or skip or duplicate pages.
- Rotating a page no longer deletes the document's unsaved annotations; they now turn with the page (#169, thanks terada-d).
- Form fields saved on systems whose decimal separator is a comma (German and most European locales) now get valid appearance streams; they previously came out blank or garbled with repeated, re-wrapped text in other viewers and in print, flatten, and export, thanks Thomas.
- Print, flatten, image export, and thumbnails no longer draw a form field twice when its stored appearance disagrees with the regenerated one.
- Installation scope is now guarded end to end: a per-user install cannot sit beside an all-users install, existing dual installs are detected with an offer to remove the inactive copy, converting to all-users removes the older per-user copy, and machine-wide uninstall requests administrator access instead of reporting success after permission failures.
- The Open dialog no longer crashes where Explorer's Quick Access cannot be read, such as under Wine and CrossOver; the pinned folders and drives still list (#210, thanks Ximelay).
- Opening a PDF from Explorer while KillerPDF is still starting no longer crashes; the file now opens once the window is ready (#202, thanks tgv123456).
- Dropping a damaged PDF on the Pages panel now offers the same repair the Open dialog offers, instead of silently ignoring the file (#203, thanks 1mk3r).
- After an install relaunch or split-pane session restore, the sidebar now attaches the active pane's thumbnail cache before the first visible frame instead of remaining blank until the user clicks a pane.
- Snapping, maximizing, or restoring the window keeps the split panes' proportions, and a sidebar you closed stays closed when a tab loads its document.
- Grid view no longer drops its last column into the next row at certain pane widths.
- In grid view, drawing on a page or clicking one of its annotations now selects that page, as a plain click already did.
- Image pickers (Insert Image, image signatures and stamps, watermark) now return to the last folder an image was picked from, instead of wherever the last PDF was opened.
- Dragging the title bar downward restores a maximized window from anywhere along the bar, including over the logo, on every theme (#206, thanks 1mk3r).
- The page list's top and bottom edge fades are restored on every theme except 98SE, which deliberately has none. Switching away from 98SE now explicitly restores them instead of carrying its zero-opacity setting into the next theme.
- The empty-state recent-files panel now responds to ordinary window resizing, hiding before it crowds the drop target and returning when the pane has enough room.
- The theme and language tooltips are no longer all caps, the VIEW shortcut category matches the other headings, and the zoom shortcuts read Ctrl++ instead of Ctrl+=, in every language (PR #216, thanks Mr-Update).
- The "Show current file size" shortcut description is translated in every language (#217, thanks Mr-Update).
- Unsigned local development packages can now exercise the complete install path, while public release launchers retain a non-bypassable digital-signature requirement.
- The hardcoded English strings identified during 1.7.4 development were translated in all twelve languages, including dialog titles, file-picker filters, error and confirmation dialogs, status messages, busy overlays, and the default DRAFT watermark text. Polish also gained the seven newest theme names (#227, thanks Mr-Update).
- The annotate settings bars (text, draw, highlight, line, shape) now reflow in single-row groups on a narrow window or split pane; anything that would need a third row collapses into an overflow chevron, least-used controls first.
- A render failure partway through streaming grid tiles no longer strands the remaining pages blank; the failed page is skipped and the stream retries once.
- Grid view opened in an unfocused split pane now fills the pane width instead of keeping a surround margin and showing a horizontal scrollbar.
- A print page range ending in a huge number no longer freezes the app, and a range matching no pages now says so and disables Print instead of spooling the whole document (PRs #222 and #220, thanks Ryokoxx).
- Checkbox labels now wrap instead of clipping in languages with longer text, and dropdown lists respect their intended maximum height (PRs #224 and #225, thanks Ryokoxx).
- The print preview's scrollbar and chevrons now follow the theme, and visiting the 98SE theme no longer leaves its gray chip color behind on other themes (PR #219, thanks Ryokoxx).
- The color picker's OK button is readable at rest on every theme; it previously only showed its label on hover, and its Cancel button and remaining tooltips are now translated (#227, thanks Mr-Update).
- On the 98SE theme, the color picker now wears the classic caption bar and raised window frame with beveled buttons, code-built dialogs are square-cornered, and the annotate settings bars dock as flush full-width toolbar bands with the proper 2px bevel instead of floating with a thin misdrawn edge and leftover film grain.

## [1.7.3] - 2026-08-15

1.7.3 corrects theme accents and restores the missing visual preview in image-selection dialogs.

### Fixed
- The active tab's ring and underline now follow the chosen accent color; they stayed on the theme's base color under any other accent, on every theme.
- Image-selection dialogs now include a live preview pane for image import, image signatures, image stamps, and Insert Image.

## [1.7.2] - 2026-08-15

KillerPDF 1.7.2 completes the split-pane viewer refactor and builds on it with seven new themes, Polish localization, book layout, Levels, expanded print controls, per-pane night mode, and a substantial round of rendering, memory, form, and interface fixes.

### Added
- Added 98SE, Ectoplasm, Decay, Mourning, Sepulchre, Delirium, and Malaise themes.
- The print dialog has paper size and paper source selectors, and its settings are organized into collapsible PRINTER, LAYOUT, and OUTPUT sections (#186, thanks demo1866 and adeit).
- Two-Page view has a book layout option: the cover page displays alone, so facing pages pair like a physical book (#193, thanks TeutonJon78).
- Comb text fields are supported: typing is capped at the cell count and the saved value places one character per printed box, like Acrobat (#158, thanks flywire).
- Clicking the status line shows the open file's size for a moment, then restores what was there.
- Text selection follows columns: dragging down one column of a two-column PDF no longer sweeps the neighboring column, and copied text comes out in column order (#185, thanks twtscurry30-ai).
- The Transform tool has a LEVELS section with black point, white point, and midtone controls for rescuing pale, hard-to-read scans. It applies the correction like the other Transform options (#174, thanks 1mk3r).
- Night-mode invert is per pane in split view: the moon flips only the focused pane, and its rail icon follows pane focus.
- Polish (pl-PL) localization, the eleventh interface language, in the language picker as "Polski" (#191, thanks Fresta24).

### Changed
- The page number shows in a corner badge that slides away when the view settles, replacing the tooltip that followed the cursor (#197, thanks Ryokoxx).
- The Outlines sidebar opens with top-level bookmarks visible and deeper levels folded, and expand/collapse choices now stick across tab switches and edits instead of re-expanding everything.
- Keyboard access and context-menu hints were audited for 1.7.2: the file-size action has Shift+F4, and applicable menus now show icons and shortcuts.

### Fixed
- The re-sharpen pass renders at device resolution instead of twice it, sharply cutting memory use on large documents, and re-renders on DPI changes (#189, PR #194, thanks Ryokoxx).
- The page bitmap cache is now budgeted in bytes (~160 MB per tab) instead of a fixed page count, cutting the other large share of memory on big documents (#189, thanks ags1234).
- Saved highlights now use the Multiply blend mode, darkening the paper behind the text instead of washing the text out with an opaque rectangle (#200, thanks playerbhr).
- The picker radio's selected dot is centered in its ring (PR #198, thanks Ryokoxx).
- The theme flyout no longer jumps when switching to or from a theme without accent swatches (#199, thanks Ryokoxx).
- Reopening a file restores its last manual zoom level (#201, thanks kilasuelika).
- Resizing split panes in grid view no longer blanks the grid and rebuilds it page by page: the stretched tiles stay visible and get their bitmaps swapped in place.
- Exported images carry the chosen DPI in their metadata instead of always reporting 96, in both the GUI export and the CLI (#188, thanks GruNostalgia).
- Dropping PDFs or images onto the Pages sidebar appends their pages to the open document (#172, thanks 1mk3r).
- Form field text no longer shows a ghost "shadow" copy behind it: the viewer stopped baking field appearances into the page bitmap underneath the live field overlays, thanks Thomas. Print, flatten, export, and thumbnails still include them.
- Rotating a page that was opened with a non-zero /Rotate no longer swaps its MediaBox on save, which permanently clipped the content. Fixed in the vendored PdfSharpCore, whose landscape media-box flip fired on read pages (#184, thanks terada-d).
- Documents opened from Explorer get keyboard focus immediately, so arrows and Page Up/Down work without clicking the window first, and horizontal scrolling from a touchpad or tilt wheel now pans the document (#196, thanks Subjuntivos).
- Double-click text editing now maps PostScript font names (ArialMT, TimesNewRomanPSMT, Helvetica) to the installed Windows family, so edited text keeps its font instead of falling back to the default (#187, thanks fo-bo).
- Machine-wide installs register the killerpdf:// handler for all users, and it now appears in Default apps under link types (#183, thanks adeit).
- Themes are entirely owned by the KillerPDF repository again. The project no longer imports a private sibling `KillerUI` folder or overlays its resources at runtime, so a standalone clone contains every theme resource it builds and displays.
- Completed the PDF viewer extraction so split panes keep independent documents, tabs, pages, tools, selections, and sidebar positions.
- Various UI and theme consistency tweaks, including clearer Black-theme surfaces and controls, consistent floating-bar borders, legible accent buttons, and balanced film grain across the themes.

## [1.7.1] - 2026-08-04

1.7.1 fixes the latest reported crashes, rendering problems, installer registration, file navigation, and editing issues, while adding perspective correction and app-link support.

### Added
- Transform can now correct trapezoidal perspective distortion in pages photographed at an angle. Turn on perspective correction, drag four corner handles onto the photographed page outline, and Apply converts that quadrilateral into a straight rectangular page at the full transform resolution. The correction composes with rotation, deskew, scaling, and flipping in the same operation (#175, thanks 1mk3r).
- KillerPDF now registers a `killerpdf://` link handler for the current user, laying the app-side foundation for the planned Chrome extension. A `killerpdf://open?url=...` link can hand a public HTTPS PDF to KillerPDF whether the app is closed or already running; downloads are size-limited and rejected unless their contents begin as a PDF. The registration refreshes itself when the executable moves.
- Open and Save dialogs now return to the last folder successfully used for that kind of operation, unless the caller deliberately supplies another starting folder. The places rail also brings in the user's pinned Windows Explorer Quick Access folders alongside KillerPDF's own editable pins, while avoiding duplicate entries (#178, thanks sheafitzek).

### Fixed
- Fit Width and Fit Page are now remembered as the preferred fit for subsequently opened PDFs, so users on smaller screens no longer have to switch from Fit Page every time, thanks Thomas.
- Owner-restricted encrypted PDFs with malformed linearization tables now pass through KillerPDF's tolerant PDFium cleanup instead of being retried through PdfSharp's fragile read-only parser. This fixes the array-index error that prevented the Fritzbox 4060 manual from opening, thanks Thomas.
- Reopening a PDF no longer reapplies a raw zoom saved for a different window or monitor size, which could make the document appear enormous or tiny. KillerPDF keeps the saved page and view mode but fits the document to the current window, with Grid returning to a predictable three-column layout. Perspective correction's corner handles now retain their drag capture across child controls and release reliably, and applying the correction immediately redraws the edited page even when the current zoom does not change.
- Multi-line highlights now follow the reading direction of Persian, Arabic, Hebrew, and other right-to-left text. The first selected line extends left from the starting point and the last line extends right to the ending point, while left-to-right documents keep their existing behavior. Direction is detected per line, so mixed-language pages work without a document-wide setting (#170, thanks playerbhr).
- Installing KillerPDF for everyone now registers its PDF handler for the whole computer instead of writing it into the elevated administrator's personal registry. Every account can now find KillerPDF in Open With and Default apps, with the shared registration pointing at the Program Files copy; each user still chooses their own PDF default (#176, thanks adeit).
- The keyboard-shortcut list now uses the available window width instead of squeezing both halves into a narrow fixed card. Longer translated descriptions have room to remain visible, wrap cleanly on smaller windows, and sit level with the shortcut text in both columns (#177, thanks Mr-Update).
- The mouse wheel now moves the file picker's multi-column list horizontally, so folders and files beyond the right edge can be reached without dragging the bottom scrollbar. The folder tree's wheel works too, scrolling vertically normally and horizontally while Shift is held. The shared picker fix applies to Open, Save, image import, signatures, certificates, and every other file-selection flow; icon and details views keep their normal vertical wheel scrolling.
- Text annotations, highlights, stamps, ink, and filled form fields already stored in a PDF now appear in KillerPDF and survive printing, flattening, image export, page transforms, thumbnails, and repair rasterization (#141, thanks zenfas). PDFium does not paint annotation appearance streams unless explicitly requested, so every pixel-producing path silently omitted them. Enabling Docnet's annotation flag was not safe because it creates and destroys a form-fill environment while its page remains in use, corrupting PDFium state and crashing on a later native call. KillerPDF now renders through its direct PDFium layer, owns the form callback memory for its full native lifetime, paints both ordinary annotations and interactive widget appearances, then immediately closes the form, page, and one-shot document together. The reporter's five-page D&D Beyond file from #179 now exports and flattens with its filled values and multiline fields intact, without the native teardown crash (#179, thanks hsnopi).
- Annotations and stamps now save in the right position on PDFs that already carry a native page rotation when they are first opened (#169, thanks terada-d). The 1.7.0 fix read only KillerPDF's temporary rotation map, but that map is not populated until a page operation performs a temporary save and reload, so annotating an already rotated file and saving it immediately still treated the page as unrotated. Burn-in now falls back to the page's own `/Rotate` value, every newly opened document clears the previous document's temporary rotation map, and saving removes a malformed CropBox that extends outside its MediaBox instead of preserving contradictory portrait and landscape dimensions. Tests cover the reported invalid page boxes and preservation of a valid inset crop.
- Filled form fields now generate complete appearance streams, including the required stream length, multiline layout, and WinAnsi text encoding (#180, thanks Ryokoxx). This keeps entered values visible and readable in PDF viewers that strictly validate field appearances, resolving the damaged-file warning, missing line breaks, and replaced punctuation reported in #179 (#179, thanks hsnopi).
- Double-clicking bold or italic PDF text to edit it no longer turns the replacement into regular text (#182, thanks fo-bo). PDF text usually carries its face styling inside the embedded font name, such as `Helvetica-BoldOblique`, rather than as separate bold and italic properties. The detector cleaned those suffixes off to find the font family, then explicitly reset both style flags before opening the editor, so the formatting was lost before the first keystroke. Font detection now separates the family from its bold and italic face, applies both to the live edit box, and carries them into the replacement annotation when it is committed. Focused tests cover subset font names and regular, bold, italic, and combined faces.
- Clicking a page no longer crashes with "'∞' is not a valid value for property 'Height'" (#181, thanks lachlan-00). The page click rebuilds every annotation and form overlay, and malformed geometry could reach a WPF Width or Height property without being checked. WPF refuses NaN and infinity, so one bad form rectangle or a legacy saved signature with zero canvas dimensions took down the whole viewer during the redraw. Form rectangles and every sized annotation are now checked before they reach WPF; invalid form widgets are skipped, old signature dimensions fall back to the standard canvas size, and the render layer has a final guard for malformed persisted annotations.

### Changed
- Transform's Rotate, Scale, Flip, Skew, and Perspective sections now collapse like the Stamp dialog, keeping the sidebar compact while still allowing every control to remain in one window. Rotate opens initially and the less frequently used sections stay folded until needed.
- Refined six German labels and shortcut descriptions for more natural and consistent wording (#150, thanks Mr-Update).

## [1.7.0] - 2026-08-01

KillerPDF 1.7.0 introduces split panes, with two documents side by side in one window. It also replaces every stock Windows file dialog, adds a themed system menu and picture-aware night mode, saves non-Latin scripts correctly, and places annotations accurately on rotated pages.

### Added
- Split pane: F10 shows two documents side by side in one window. Each pane is a card of its own, and the focused one carries an accent ring so it is obvious which pane the toolbar, sidebar and page list are acting on. Click either pane to move focus. The boundary between them has a handle on each side: grab the left one to size the left pane or the right one to size the right. Neither pane can be squeezed below a readable width. F10 closes the split again, and the rail button's icon follows along: a pane pushed out will open, while one pulled back in will close. Each pane has its own tab strip, but the shared toolbar, sidebar, and page list follow whichever pane has focus. Drag a tab from one pane's strip to the other to move it across. On a maximized or snapped window, F10 splits the space evenly instead of squeezing the second pane to its minimum.
- Every Open and Save dialog is KillerPDF's own now, instead of the stock Windows one: the same themed window as the rest of the app, with a places rail, a folder tree, list/icon/details views, sortable columns, pinnable folders and recent locations. That covers opening and saving PDFs, merging, extracting pages, flatten, image export, image import, signature and certificate picking, OCR output and the zip export - including picking several files at once where that applies. Shared with the other Killer Tools apps, so the file dialog looks and behaves the same across the family.
- Install for everyone on this computer. The Install button on the portable badge now opens a confirmation with two choices: add a desktop shortcut (on by default, as before) and install for all users (off by default because it needs an administrator). An all-users install puts KillerPDF in Program Files with a Start menu entry for every account, and removes the per-user copy so there is a single entry in Add/Remove Programs rather than two. Declining the administrator prompt leaves the app running portable exactly as it was. A `/silent` switch performs the machine-wide install with no interface for winget, Chocolatey, and RMM deployment. This matches Killendar and KillerShell. PDF file associations are still registered only for the current account; an all-users install does not change what PDFs open with for anyone else.
- "Confirm before opening links" is back, on the About card beside the recent-files toggle. It asks before a link in a PDF opens in your browser, and it is off by default so links stay immediate unless you want the check. The prompt's own "Don't ask again" now simply switches the same option off, so the two can no longer disagree. The setting had no home after the Settings panel was dissolved; About is where the safety and data-hygiene controls live.
- Ctrl+Shift+W closes every tab except the current one, alongside Close Tab on the tab's right-click menu - both now line their keyboard shortcuts up in a real column instead of each item sizing its own.
- Right-clicking the title bar (or Alt+Space) shows a themed system menu that matches the app instead of the stock white Windows one. Same items, same behavior, in all ten languages.

### Changed
- Internal: the document view is now a self-contained control rather than part of the main window - the page rendering, zoom, annotations, text editing, crop, forms, links and text selection all moved into it, about 8,700 lines. Every function moved verbatim, verified line by line against the previous version, and no behavior change is intended. This is groundwork for showing two documents side by side in one window.
- The document area is a rounded, lifted card like the rest of the Killer Tools apps, instead of a squared pane running flush into the window edges: rounded corners, an 8px inset on its outer side, and a real drop shadow that falls across the status bar. The sidebar's five-dot gripper is gone, replaced by the family divider - a thin line that lights up in the accent color when you hover or drag it. Full screen still fills the display edge to edge.
- Night mode no longer inverts pictures (#135, thanks dmantisk). Photos and figures keep their real colors while the page around them goes dark, matching the behavior requested from Okular. Right-click the moon (or press Shift+N) for "Invert images too" if you want the old full inversion back. This is useful on scanned documents, where the whole page is one image. Night mode only changes what you see on screen: saving, printing and exporting still produce the document's original colors.
- The Settings panel and its gear are gone: every section moved to where the thing it configures lives, matching the rest of the Killer Tools apps. Theme and language are flyouts on new rail buttons (below the night-mode moon, with a ? button for the shortcuts overlay); the theme flyout stays open across a pick so themes can be compared.
- Toolbar appearance is a right-click menu on the toolbar itself, and it is two independent choices now instead of one list of five: icon size (small/large) and text placement (none/beside/under/text only) - so large icons with captions is finally possible. Fresh installs default to large icons with text underneath; existing installs keep their setting. Ctrl+Shift+1-6 pick the options directly.
- Internal: the codebase has been reorganized into the Killer Tools family layout - document logic in service classes, the About/CLI/OCR/search features behind controllers, the window partials under Shell/. Every moved function is verbatim and no behavior change is intended; the repo root now holds only the entry files.
- New app icon. The old document icon with the red bar across the bottom now marks PDF *files*, so a KillerPDF window and a PDF sitting in a folder are no longer the same picture. Explorer caches icons aggressively, so a PDF may keep showing the old art until the cache refreshes.
- View mode is a rail button wearing four view tiles, one per layout: click for a flyout (each mode with its F-key beside it), roll the wheel over it to step through the views, or press F9 to jog from the keyboard - F5-F8 still jump straight to one, and Ctrl+, is retired. All the new shortcuts are on the F1 overlay, in all ten languages.
- Internal: the tab strip and split-pane drag/focus model were rewritten to match KillerShell's implementation, the family's reference for both, replacing the original hand-built version.
- The sidebar's left/right choice sits at the bottom of the sidebar's right-click menu, which now opens from any part of the sidebar; Ctrl+Shift+B flips the side, pairing with Ctrl+B's collapse.
- The content pane's border is a shade lighter in every theme, so the edge between the pane and the chrome reads more clearly - the same value the other Killer Tools apps use.
- The prompt offering to make KillerPDF your default PDF viewer is translated now; it was English-only in every interface language.
- The sidebar's page list fades into the background at its top and bottom edges while there are pages scrolled past them - the same treatment KillerShell's folder tree and the killerpdf.net sidebar use. Each fade ramps in over its own height as a row slides under it, so nothing pops, and neither shows when the list is flush at that end.

### Fixed
- Japanese and other non-Latin text no longer saves as empty boxes (#168, thanks terada-d). The editor is a Windows text box, which quietly borrows glyphs from any installed font, so what you type always looks right; the save path resolved a single font and wrote a box for every character that font lacked. Two things were wrong. Nearly every CJK font on Windows ships as a collection file (Yu Gothic, MS Gothic, Meiryo, YaHei, JhengHei), and the save path could not read collections at all. Even picking a Japanese font by hand did not help. Nothing checked whether the chosen font could carry the text either. KillerPDF now reads collection fonts, and when your font cannot render something it picks one that can, preferring the same faces Windows itself falls back to. Your own font is always used when it covers the text. This affects Bengali, Korean, Chinese, Thai, Arabic and Indic scripts too, and applies to page numbers and watermarks as well as placed text. Embedded fonts are subset, so a line of Japanese adds tens of KB to the file rather than the whole typeface. If nothing installed can draw a character, KillerPDF now says so when the text is placed and lists the unsupported characters. This rare case can happen when one box contains two non-Latin scripts or the PC has no font for the requested script.
- Punctuation shortcuts work on keyboard layouts that need Shift for those characters (#153, thanks Mr-Update). Shortcuts were matched by key position, which is a US-layout assumption: on a German keyboard "?" lives on Shift+ss and "=" on Shift+0, so Ctrl+? and Ctrl+= pressed keys the app was not listening for, and the extra Shift broke the match a second time. Zoom and the shortcuts overlay now respond to the key that TYPES the character, whatever position it occupies, so they work on German, French, Nordic and other layouts rather than being fixed one at a time. The shortcuts overlay also prints the spelling that is right for the keyboard in use instead of always showing the US one.
- Annotations and stamps are no longer burned into the wrong frame on a rotated page (#169, thanks terada-d for a report that diagnosed it down to the line). A page's rotation is deliberately kept outside the working document, so the canvas you draw on is in the rotated frame while the save path writes into the page's own unrotated one - and the save path was never told the angle. Anything placed on a quarter-turned page came out rotated 90 degrees from where you put it, offset, and scaled on swapped axes, which also squeezed text boxes narrow enough to wrap after almost every character. Stamps had the same fault, so page numbers and watermarks landed in the wrong corner and disagreed with their own preview. The rotation now travels with the burn, in the editor and in the background flatten that print uses.
- The app-size readout parked itself on the status bar. Rolling the wheel over the logo wrote "App size N%" into the footer behind a short hold, which existed so the chrome resize could not stomp the message with its own page and zoom status the same frame. When that hold expired nothing repainted the line, so the readout stayed put until the next page change, tool switch or open happened to write over it. It is transient now. Each notch rewrites the readout and restarts a five second timer, and the status line goes back to what it was showing before the first notch when the timer expires. The hold is unchanged and still only covers the same-frame stomp. If something else wrote a status after the hold lapsed the restore is skipped, so a real message is never replaced by a stale one.
- Editing a line of text could collapse it to 3pt (#163, fixed in #165, thanks Ryokoxx). The font size was read from the size written in the content stream, which is only the visual size when the text matrix does not scale - a generator that emits `/F1 1 Tf` and applies the scale through the matrix reported 1, and the replacement text hit its lower clamp. The point size is used now, falling back to the old value and then to the line-height estimate, since the point size can be zero on fonts with no usable metrics. Covered by tests that pin both spellings.
- Rotating a quarter-turned page by a few degrees no longer squashes it back to portrait (#167, thanks japsmits). The transform rendered the page with its rotation but sized the result from the unrotated page box. On a landscape page, that disagreement stretched the result vertically to fit the old portrait shape. The page dimensions now follow the rendered orientation in both page-size modes.
- Ctrl+0 and Ctrl+1 did not reset the zoom to a true 100% (#154, thanks Ryokoxx). The internal zoom level scales each page's layout box, and outside Continuous that box is the render-dimension bitmap rather than the page's natural width - so asking for 1.0 landed near 200% in Single, Two-Page and Grid. Absolute zoom requests now convert through the same display factor the zoom dropdown presets already used, so 100% means 100% in every view mode.
- A signature dropped onto a fill-in form field is no longer hidden behind it (#156, thanks Peter5164). Redrawing a page paints the annotations first and then restores the interactive field overlays on top of them, so anything placed over a field vanished underneath it. The field overlays now sit below the annotation layer; they stay clickable, since annotation visuals never intercept the mouse.
- The page-number tooltip now shows on every page in every view mode (#151, thanks Mr-Update). It was only ever set on the secondary page tiles, so Single and Continuous had none, Two-Page only showed it on the right-hand page, and Grid started at page 2.
- Text edit could not pick up a line's font when the letter data left the name blank (#166, thanks Ryokoxx). The fallback read the font name off the word, which joins its letters' names into one string ("Helvetica Helvetica Helvetica ..."), so nothing could resolve it and the edit box landed on the default font - the same result as having no fallback. It reads the letter's name only now.
- The odd/even page filter never reached the print job (#159, thanks Ryokoxx). The preview and the sheet count both read the filtered page list, but the print path re-parsed the typed range on its own and so printed every page in it. All three now walk the same list, and "print odds, flip the stack, print evens" works as intended.
- A link annotation with no /Subtype entry no longer aborts the pre-save link-border strip with a NullReferenceException mid-save - it is skipped like any other unreadable annotation. Surfaced while the scrubs moved to their service class; the old code dereferenced the missing entry before checking it.
- Picking a color with the screen eyedropper and pressing OK could silently throw the pick away - the tool then kept drawing whatever color last got through, which read as "shapes ignore the color I chose". The eyedropper opens a second modal window inside the color dialog, and closing that inner window could corrupt the outer dialog's OK/Cancel result, so a real OK came back as a cancel. The dialog now reports its committed color through its own flag instead of trusting that result. The eyedropper button also gained a proper hover and a lit armed state while the crosshair is active, and no longer shows the crosshair cursor just for hovering it.

## [1.6.6] - 2026-07-23

KillerPDF 1.6.6 is primarily a bug fix release. Most importantly, it corrects form fields that appeared in the wrong place on non-A4 documents. It also remaps tool hotkeys, adds Remove Password, and includes several menu, keyboard, and interface improvements.

### Added
- Remove Password in the Save dropdown (#149, thanks dmantisk): saves the open document back over the original with its password protection dropped - available whenever the file needed a password (or carried owner restrictions) to open. KillerPDF already strips encryption at open time because the editing pipeline cannot modify encrypted files in place, so every save has always written an unprotected PDF; this makes that behavior a visible, deliberate action, and regular saves of a previously protected file now say so in the status bar instead of dropping the password silently. In all ten languages.

### Changed
- Tool hotkey remap - the digits again mirror the toolbar left to right, with Shapes slotted in (breaks some muscle memory; the letter keys are unchanged): V = Select (the Photoshop / Illustrator / Figma convention; its old digit went to Text), 1 = Text, 2 = Highlight, 3 = Line, 4 = Shapes, 5 = Draw, 6 = Image, 7 = Signature, 8 = Crop, 9 = Transform, 0 = Stamp. The toolbar buttons reorder to match (Highlight now before Line, Shapes between Line and Draw), and the Shapes tool has a keyboard shortcut for the first time. The shortcuts overlay (both views), tooltips, and the help page follow.
- Invert document colors moved from Ctrl+I to the bare N key (night mode), freeing the conventional italic chord: Ctrl+B / Ctrl+I / Ctrl+U now toggle Bold / Italic / Underline while typing in a text box, matching the text bar's B/I/U buttons. Listed in the shortcuts overlay in all ten languages.
- Esc now steps down instead of straight out. With nothing left to cancel, it first returns to the Select tool, Acrobat-style, and only a second Esc exits the app as before. The Highlight tools' hint on a page with no text layer now points at the deliberate rectangle path: "No text here. Shapes is on 4." The message is translated into all ten languages.
- The right-click menus caught up with 1.6.5's menu polish: every item in the page, annotation, sidebar-thumbnail, and background context menus now carries its icon in the left gutter, matching the toolbar's glyph for the same action - and page rotation gets a proper mirrored CW / CCW pair.
- The page sidebar now starts collapsed when no PDF is open because an empty workspace has no thumbnails to show. It opens when a document loads and collapses again when the last document closes. The empty page-number box and "/ -" that used to sit in the sidebar header are also hidden until a document is open.

### Fixed
- Interactive form-field overlays sat in the wrong place on any document that is not A4-sized - shifted down and slightly wide, worst near the top of the page, while the page itself (and every other viewer) drew the fields correctly. PdfSharpCore's page.Width getter, which the link layer touches on every render, silently converts the parsed /MediaBox array into its internal rectangle type; the field parser's array read then came up empty and fell back to a hardcoded A4 page size, so only A4 documents lined up. The field parser now reads both representations and walks the page-tree inheritance chain for /MediaBox and /CropBox. Found through the brochure: the shipped copy is A4, so the bug was invisible until a US Letter rebuild put every field about 40 points adrift.
- The Document Info shortcut label showed mojibake in Spanish, Bengali, and both Chinese interfaces - the same double-encoding repaired for Japanese in 1.6.5 (#136). All four now render their real text.
- Exported JPEGs no longer come out as black pages, and exported PNGs no longer carry a transparent background (#148, thanks Ryokoxx). PDFium leaves unpainted background pixels fully transparent. The JPEG encoder dropped that alpha channel and kept the zeroed color underneath, so most PDFs rendered solid black through `--to-image --format jpg` and the new Export pages as images dialog. Exports now composite over white by default, which also keeps the needless full-page alpha channel out of flattened PDFs (`--flatten` and Save Flattened). A new `--transparent` flag on `--to-image` keeps the raw alpha for PNG output when transparency is actually wanted.
- The Password Required prompt now matches the rest of the app, with a wordmark title bar, a dark film-grain card, a themed password field, and Open and Cancel buttons. It replaces the stock white Windows dialog and native chrome.

## [1.6.5] - 2026-07-22

### Added
- Shapes tool: rectangle, ellipse, and free-form polygon markers, each with an optional fill. Box keeps the classic drag-a-filled-rectangle gesture the highlighter used to have; ellipse and polygon are closed outlines that move, resize, flatten, and print like any other drawing. Freeform places points click by click - click the first point (its target lights up when you are close) or double-click to close, Esc cancels, Backspace removes the last point. The tool shares the draw bar's color, size, and opacity, with a mini-shape sub-mode picker and a Fill toggle.
- Export pages as images (#132, thanks KaneLeung): a new entry in the Save dropdown renders pages to PNG or JPEG files at a chosen DPI (24-1200, default 150) with an optional page range, through the same pipeline as the CLI's `--to-image`. Pending annotations and stamps are burned in, in-app rotations are honored, and files land as `<name>-page-001.png` next to the base name you pick.
- Odd/even page printing (#134, thanks superaustingao): a new selector under Pages offers All pages, Odd pages only, and Even pages only. It filters the chosen page range, and the preview follows along. Print the odds, flip the stack, then print the evens for manual duplex on printers without a duplexer.
- Invert document colors (#135, thanks dmantisk): a moon toggle at the bottom of the sidebar rail (or Ctrl+I) renders the document with inverted colors for dark-mode reading - the icon lights in the accent while active, and the choice is remembered across launches. Display only: saving, printing, exporting, OCR, and the sidebar thumbnails all keep the document's true colors.
- App-wide size control for accessibility, the KillerNotes way: the title bar now shows the app icon next to the wordmark. Scrolling the mouse wheel over that logo scales the toolbar, sidebar, and tab strip in fine steps from 70% to 250%. Ctrl+Shift with the plus or minus key adjusts it from the keyboard, and Ctrl+Shift+0 resets it. The setting is remembered across launches. The document pane is deliberately untouched: app size and page zoom stay separate controls, so scaling the chrome never changes what the page looks like. It uses a layout scale so UI text stays sharp, and the title bar and footer stay fixed so the logo never moves out from under the cursor.
- Recent-files privacy controls (#146, thanks Bolle1987): a Clear list link on the start screen's Recent panel (matching the one already in the Open dropdown), and a "Don't remember recently opened files" toggle in the About window next to Clear all Data, where the data-hygiene controls live - turning it on also empties the existing list, so nothing about your documents persists on a shared machine. Translated into all ten languages.
- Czech (cs-CZ) localization (#138, thanks jiri-ops): the tenth interface language, a full translation following Czech Windows/Adobe conventions, in the language picker as "Čeština" - with Czech ("ces") joining the OCR language catalog, downloadable on demand like the rest.

### Fixed
- Page numbers and watermarks are now written into the saved PDF when they are the document's only markup (#147, thanks Mr-Update). Every save path burned the stamp layer only when the document also carried an annotation. As a result, stamping a clean document produced a PDF with nothing on it.
- Stamps can be removed again (#145, thanks Mr-Update). Unchecking both Page Numbers and Watermark disabled the Apply button, so once a document had stamps there was no way to turn them off - applying with both sections off is exactly how they are cleared, and is now allowed whenever the document already has stamps.
- Fixed a crash when opening Stamp or Transform, or saving a page with a multi-line text annotation (#142, thanks TrNguyen20; root cause and fix from Ryokoxx in #144). The burn silently used justified alignment, whose draw path dereferenced the empty line-break blocks produced by a newline. Burned text is now explicitly left-aligned, finally matching the editor once a line wraps. The vendored formatter also skips line-break blocks and no longer flings blocks off the page on single-word justified lines. The typeface behind a text box resolves lazily, so a font can still fail at first draw on a machine missing that face. In that case, the draw falls back to the stock font and then skips only the failing annotation. A failed preview burn renders the page without its annotation layer instead of taking down the app.
- The pre-save signature scrub tripped a NullReferenceException on every save of a document with no form fields (a fresh blank document, most PDFs without forms) - swallowed silently in release builds, but it aborted the scrub early and broke into any attached debugger. Absent dictionary entries like a missing /AcroForm are now treated as "not there" instead of dereferenced.
- Bookmarks that point at named destinations now resolve (#143, thanks Ryokoxx). PDFs from HTML-to-PDF generators (wkhtmltopdf underneath most invoice and statement tools) write outline destinations as names looked up through the catalog, which the outline loader did not handle - Debug builds popped an assertion dialog and Release builds left the bookmark silently dead. Resolution now falls back to the same name-tree walker the link layer already uses.
- The sidebar page thumbnails, outline tooltips, and grid-view tooltips always said English "Page N" regardless of the interface language (#137, thanks jiri-ops) - the labels are now real localized strings in every language, and they update immediately on a language switch.
- Japanese: repaired a garbled Document Info shortcut label (mojibake) and tightened the About wording (#136, thanks coolvitto).
- Fresh clones build again without manual repair: an explicit .gitattributes rule keeps EOL normalization away from the vendored third_party sources (#140, thanks Ryokoxx), belt and braces on top of the earlier re-encode.
- The Shapes tool strings and the outline's "(untitled)" placeholder existed only in English and Czech - the other eight languages showed blank tooltips and labels there. All ten languages now carry the full string set, verified key-for-key against English.

### Changed
- Text selection now flows with the text (#127, thanks Ryokoxx): dragging with the Select tool tracks the actual run of characters in reading order, browser-style - across lines, paragraphs, and (in continuous view) across pages. A plain click still selects annotations, and drags that start on empty page keep the classic box select, so scans and annotation multi-select behave as before. Ctrl+A now shows real per-line selection on the page.
- Highlight, Strikethrough, and Underline follow the text the same way: drag over words and the markup hugs each line instead of laying down one rectangle. One gesture produces one grouped annotation per page - it selects, moves, deletes, and undoes as a single unit. On pages with no text layer the tools show a status hint instead of silently drawing a box; the highlight eraser keeps its rectangle.
- Black theme: the on-page selection color was a stray royal blue; it is now a readable dark green matching the theme.
- The form-field font-size stepper is now an "inline flyout" - a new style for controls that float on the document itself: a translucent rounded pill that drips down from the field being typed in, follows it through scrolling and zoom, flips above it at the bottom of the pane, and solidifies on hover. Subtle enough to sit on a legal document without being in the way, and it can no longer collide with the draw/text bars or the toolbar.
- Menu polish: dropdown items can carry icons in the gutter the check column always reserved (Save, Open, and OCR menus got them), and the OCR "Use High Quality Models" toggle now keeps the menu open, refreshing its checkmark and the per-language "(download)" labels in place.
- Tooltips now show their keyboard shortcut everywhere one exists, in all ten languages: the whole tool palette carries its single-key hint (V select, T text, H highlight, D draw, L line, I image, G signature, C crop, R transform, S stamp), and the invert and app-size controls show Ctrl+I and Ctrl+Shift+=/-/0. The shortcuts overlay's list view also caught up with the keyboard view: Ctrl+Shift+Z (redo), Ctrl+Shift+Tab (previous tab), and F2 (rename bookmark) are listed now.
- Collapsing and expanding the sidebar is now a smooth slide instead of a snap: the panel glides shut over a quarter second with the thumbnails holding their size (clipped, not squished), and the document settles in a single crisp pass afterwards - the same pipeline a splitter drag uses.

## [1.6.4] - 2026-07-17

### Added
- Full command-line interface: `--merge`, `--extract-pages`, `--split`, `--decrypt`, `--to-image`, `--flatten`, `--print`, `--ocr`, `--version`, and `--help` run headlessly with meaningful exit codes, work while the app window is open, and reuse the exact pipelines the GUI runs (merge link rewriting, pre-save scrubs, lossless PDFium decrypt, rotation-safe 150/300 dpi rasterizing, searchable-PDF OCR with on-demand language download). See the Command Line section on the help page.
- Bookmark editing in the sidebar Outline panel (#133, thanks alivio-israu): add via the row at the top of the tree (named in place), inline rename, child bookmarks, reorder, retarget, and delete - with Ctrl/Shift multi-select, Delete and F2 keys, delete all, and full Ctrl+Z undo. Hidden on read-only files.
- Redo: Ctrl+Y (or Ctrl+Shift+Z) re-applies undone actions - annotations, text edits, stamps, clears, and document-level operations alike. Any new edit clears the redo chain, and redo history is kept per tab.
- Jump history: Alt+Left / Alt+Right and the mouse back / forward buttons retrace bookmark, link, jump-box, and Home/End jumps, browser-style.
- Keyboard view in the shortcuts overlay (F1): a visual keyboard with every bound key lit and color-coded by category. Toggle LIST / KEYBOARD in the header (the choice sticks), click a layer or hold Ctrl / Shift / Alt to preview it, and hover a lit key for its action. Follows the active theme and language.
- More conventions from the big viewers: Home / End jump to the first / last page, Ctrl+1 / Ctrl+2 / Ctrl+3 set actual size / fit width / fit page, and the Menu key or Shift+F10 opens the right-click menu at the current selection (keyboard accessibility).
- Japanese OCR language (`jpn`), downloadable on demand like the rest - the OCR language list now covers the same nine languages as the interface.
- Command-line batch mode: `KillerPDF.exe --batch-resave <input> <output> [--log report.csv] [--quiet]` resaves a single PDF or a whole folder tree headlessly through the standard open/save pipeline, with per-file OK/SKIP/FAIL reporting. Built for the validation harness.
- Standards-conformance validation harness (`validation/`): `Compare-VeraPDF.ps1` diffs two veraPDF batch reports (corpus baseline vs a `--batch-resave` output tree) and flags any file whose validation outcome a KillerPDF save changed. Verifies that saving through KillerPDF does not degrade PDF/A conformance.

### Changed
- Shortcut remap: About moved from F2 to F12, and Document Info moved from F12 to F4 (Ctrl+D also works, matching Acrobat/Foxit/Sumatra's Document Properties). F2 now renames the selected bookmark in the Outline panel, the Windows rename convention. Settings gained F9 (Ctrl+, also works, the VS Code / Windows Terminal convention), and F3 / Shift+F3 step to the next / previous search match from anywhere (F3 opens search when it isn't). Pressing a dialog shortcut while the shortcuts overlay is open dismisses the overlay first. The shortcuts overlay and the help page keyboard map follow.
- Keyboard shortcut hints audited app-wide: menus now show their shortcut dimmed at the right edge wherever one exists (OCR, close tab, bookmark rename/delete, and more), the help tooltip advertises F1, and missing tooltip hints were added in all nine languages (OCR Ctrl+Shift+O, sidebar collapse Ctrl+B, grid view F8).
- Continuous view: clicking a page no longer snap-scrolls its top to the viewport. Clicks in the document are for tools and selection only, and the current page follows the viewport as you scroll - the convention the big viewers use. The sidebar, jump box, links, bookmarks, and page keys still jump as before (#128, thanks Ryokoxx).
- German translation refinements: Dokumentinfo, Zuschneidebereich for CropBox, Entf for the Delete key (thanks Mr-Update, #126).
- The sidebar tab is labeled OUTLINE (singular) in English, matching the other languages.

### Fixed
- Resaving a PDF no longer reduces its PDF/A conformance. The PDF library (PdfSharpCore, MIT) is now vendored under third_party/ with six patches: no Producer/Creator stamping into an imported document's Info dictionary, no /ModDate rewrite at open, no transparency /Group injected into every page, stream /Length now always matches the spec's byte count (empty streams included), boolean values written as the spec's lowercase true/false keywords, and the debug-only verbose file layout removed. Found by the new veraPDF validation harness across a 2,900-file corpus.
- Intermittent hard crash (native heap corruption) while scrolling or clicking through a document, most visible on annotation-heavy pages: KillerPDF's direct PDFium calls (link extraction, encryption stripping) could land at the same moment as a background page render inside PDFium, which is single-threaded. Every direct call now holds the same lock the render path uses. Diagnosed from a 1.6.3 crash dump showing two threads inside PDFium at once.
- Saving a PDF that carries a digital signature kept the old signature value even though any edit breaks its digest (which must cover the entire file), so strict validators rejected the result. Saves now strip dead signature values and the matching /Perms entry; the signature fields themselves are kept.
- Saving over the open file failed with "being used by another process" on PDFs whose pages carry annotations but no links readable by the primary parser (typically fillable forms): the cached PDFium link handle was holding the file open. It is now released before every save (#129, thanks Peter5164).
- Opening a PDF whose page tree parses to zero pages crashed Continuous view with an out-of-range page index; it is now guarded (#130, thanks demo1866).
- Bookmark titles in password-protected PDFs showed as mojibake (a stray BOM prefix followed by garbled characters) instead of their Unicode text - most visible on Chinese outlines. Titles the parser hands over raw are now re-decoded for display (#133, thanks alivio-israu).
- Grid view never tracked the current page while scrolling, so the statusbar counter, the page jump box, and the page a new bookmark targets could all point at a page long since scrolled away. Grid now follows the tile nearest the viewport center, like Continuous.

### Security
- Image codec library SixLabors.ImageSharp updated from 1.0.4 to 2.1.13, clearing all seven published advisories against the old version (denial-of-service and out-of-bounds issues in image parsing). Image import, clipboard paste, and signature images all pass untrusted files through this library.

## [1.6.3] - 2026-07-12

### Changed
- Links open directly again: the confirm-before-opening prompt and its Settings row are off for now.
- When both document scrollbars are visible, the vertical bar now runs the full pane height and owns the corner.

### Fixed
- Switching from Grid to Continuous view kept the grid's scrollbar overrides, clipping zoomed pages with no horizontal scrollbar. Continuous now restores its own scrollbar setup.
- Closing with unsaved changes stacked two prompts. Confirming "close without saving" now counts as the quit confirmation, and the prompt defaults to No so a stray Enter can't discard new work.
- Saving any PDF whose pages had no crop box silently planted a zero-size /CropBox on every page, which Adobe rejects with a "page dimensions out-of-range" error - the real reason merged Google Docs exports failed in Acrobat but opened in Chrome. Page boxes are now read without touching the document, and every save strips degenerate crop boxes, so re-saving a file damaged by 1.6.x heals it (thanks Richard Lam).
- The quit prompt no longer appears when no documents are open - an empty window just closes.
- Saving any PDF that has no bookmarks silently corrupted the file's structure (a dangling /Outlines reference). Strict viewers refused the file with a repair prompt, and the repair stripped fillable forms. Saves are now clean, and the repair path first tries a lossless PDFium re-save that preserves forms and bookmarks, so files damaged by older builds recover intact (#103, thanks Peter5164).
- Two-Page mode: arrow keys, PgUp/PgDn, and the wheel's edge page-flip now move one spread at a time instead of one page (#120, thanks eddardburger).
- Selection boxes drawn with the Select tool could get stranded on screen until the app was restarted. They are now removed from the layer they actually live on, and closing a file sweeps any stragglers (#121, thanks TaBnLd).
- High memory use on large documents (#122, thanks RoyYang567): the per-tab page-bitmap cache is now capped to a window of pages around the viewport, closing a tab compacts the heap so RAM visibly drops, and Continuous view only holds bitmaps for pages near the viewport - a 243-page image-heavy PDF now costs a few hundred MB instead of climbing past 7 GB.

## [1.6.2] - 2026-07-11

### Added
- Page Up / Page Down navigate to the previous / next page regardless of what has focus. Page reordering stays on the toolbar Move Up / Move Down buttons (#117).
- Japanese (ja-JP) interface translation, selectable from the language picker (#118, thanks coolvitto).

### Changed
- Footer/status bar tightened to match the killerpdf.net statusbar: 4px shorter with larger text, and the corner grip dots now stay visible when the window is maximized or snapped.
- Ctrl+scroll zooming is smooth: each wheel notch zooms by a constant 10% ratio, the view scales instantly while the wheel is moving, and the crisp high-resolution re-render happens once when the wheel rests. Precision touchpads glide proportionally.
- Up / Down arrows now scroll the view like the mouse wheel, flipping pages at the top or bottom edge. Left / Right and PgUp / PgDn remain hard page jumps.
- Status-bar and dialog messages that were still shown in English now follow the selected language across all nine locales.

### Fixed
- Switching view modes now cross-fades instead of cutting instantly, with no intermediate-frame flashes.
- The in-app self-updater now reads `SHA256SUMS.txt` from the release assets instead of the repo at the release tag, so the hash can no longer drift from the binary and fail the update's checksum.
- Importing images with broken DPI metadata (common in WhatsApp photos and some scans) produced pages Adobe Reader refuses to display; imported image pages are now kept within Adobe's supported 3-14,400 point range (thanks Richard Lam).
- Saving a document that already contains out-of-range pages now offers to scale them to a supported size; the pages keep their look and proportions.

## [1.6.1] - 2026-07-01

### Added
- On quit with documents open, KillerPDF asks whether to reopen them next launch, with a "remember my choice" option (#105).
- Enter and Esc now confirm and cancel dialogs (#111).
- Right-clicking the Open, Save, and OCR toolbar buttons opens their dropdown menu (#109, thanks Ryokoxx).
- Copies and custom Scale in the print dialog are numeric fields with an up/down spinner, arrow-key and wheel stepping (#109, thanks Ryokoxx).
- The print dialog remembers the last printer, orientation, color, and two-sided choice (#109, thanks Ryokoxx).
- Improved German translation (#114, thanks Mr-Update).

### Changed
- Mouse wheel scrolling is faster in all view modes and the page sidebar.

### Fixed
- Continuous view stays sharp when zooming in and on high-DPI displays; visible pages re-render at a higher resolution (#85).
- Open menu: the remove (X) button on each recent-files entry was clipped off the right edge of the dropdown; it now stays inside the frame.
- Crash when saving a freshly merged or imported PDF (#112).
- Save failing with "Cannot retrieve stream length"; the file is now recovered automatically (#106).
- Startup crash on older Windows 10 / .NET Framework builds (#101).
- Toolbar dropdown carets (Recent files, Save, OCR) missing on Windows 10 (#104, #108, thanks again Ryokoxx).
- Extra copy when printing multiple copies on some printers (#83, #107).

## [1.6.0] - 2026-06-27

### Added
- Tabbed documents: open several PDFs at once, each restoring its page, zoom, and view mode. Drag tabs to re-order.
- OCR built into the single exe (Tesseract): OCR a whole page or a dragged region to the clipboard, Make Searchable PDF (an invisible text layer over the scan), and Extract All Text to a .txt or .md file. A language picker downloads extra languages on demand, with an optional high-quality model toggle.
- Digital signatures with a cloud certificate (Certum SimplySign): reusable signatures and initials, click-to-sign form fields, and a movable Signatures popup that remembers its position.
- Transform tool: rotate in 90-degree steps or by a fine angle, scale, flip, and straighten a crooked scan by drawing a line along anything that should be level, all with a live preview. Annotations on the page follow the transform.
- Annotation tools: Line tool plus refreshed draw and highlighter bars, each with its own color, opacity, and width; resizable, word-wrapping text boxes (double-click to re-edit) with an optional whiteout background fill.
- Select tool moves and resizes any annotation, Shift+click to multi-select, marquee-selects across page boundaries, and reopens an annotation's bar to restyle it in place.
- Full RGB color picker on every swatch row: saturation/value square, hue strip, RGB/hex inputs, a screen eyedropper, and an editable palette.
- Print options: scale, position, margins, pages per sheet, color / black-and-white, and two-sided.
- Page-number stamping from the right-click menu (start value, format, position, size) as one undo.
- Drop a folder or .zip archive onto the window to open the PDFs and images inside, choosing to merge them into one PDF or open each in its own tab.
- Document Info dialog (F12): view and edit a PDF's title, author, subject, keywords, and creator metadata.
- Recent files: a dropdown by Open (last 10) and on the start screen, plus a Save / Save As dropdown; each entry carries its real Windows file-type icon.
- Keyboard shortcuts for tools, views, and panels (F1 shortcuts list, F2 About, Ctrl+V paste, Esc to close, F5-F8 view modes, F11 fullscreen...); the overlay lists them all and links to the full online guide.
- Full-screen mode (F11): hides all chrome so only the document fills the monitor, with a black fade in and out.
- Per-field font size while filling text fields, baked into the saved PDF.
- One-click update from the About dialog when a newer release exists.
- Toolbar style picker: small or large icons, text beside, under, or only.
- Sidebar is resizable and can be placed either left or right, with the collapse toggle, splitter, and Settings flyout mirroring to match.
- Accent colors (red, orange, green, teal, blue, purple) for the Dark, Light, and Black themes, each remembered independently.
- "Clear all Data" link in the About window to wipe settings, downloaded OCR language models, and temp files.
- Bengali, Turkish, Simplified Chinese, German, and French translations (contributors akib-h #79, mrantikadev #76, KaneLeung #82, Dtrieb & Gevlug #93, Thalis-fr #95).

### Changed
- Visual refresh: new logo, wordmark, app and PDF-file icons, fonts, and colors throughout.
- Blood, Greed, and Cyanotic use darker chrome with a lighter document pane; the signature windows are fully themed and reload on theme change.
- Settings is now a slide-out accordion (Language, Theme, Toolbar, View Mode, Sidebar) that stays open after a pick.
- Crop tool rebuilt as a single docked, slidable bar matching the annotation bars.
- Text-over-text editing drops an opaque cover (fill sampled from the page) with an editable box on top; the pair can be unpaired, and image-only pages get a manual cover and box.
- Unified the page-rendering pipeline so annotations, search highlights, and tools behave identically across Single, Continuous, Two-Page, and Grid views.
- Grid and Two-Page pages render sharper on high-DPI displays.
- Restored sessions load tabs lazily, and placed images no longer re-decode while being dragged.
- Save Flattened opens the source PDF once instead of per page (Issue #68).
- Internal refactor: the ~15,000-line MainWindow code-behind split into ~40 focused partial-class files, no behavior change.

### Fixed
- Prints now rasterize at a true 300 DPI instead of the preview's ~140, so output is sharp; the preview itself renders lighter and only the pages being printed are re-rendered at full resolution, keeping memory in check on large files (Issue #83).
- Printing and Save Flattened no longer crash on documents PdfSharpCore can't reopen; they use the same repair fallback as Save.
- Opening an encrypted PDF or repairing a damaged one runs on a background thread instead of freezing the window.
- A manually-closed PDF no longer reopens on next launch (Issue #75).
- Form fields appear and fill in every view mode, align on pages with an inset CropBox or offset origin, and size their text from the field's own /DA.
- Grid view: the wheel keeps scrolling after a zoom or column change, page jumps fit correctly (Issue #78), and annotations commit to the page they were drawn on.
- Undo removes one item per press; a held Ctrl+Z no longer fires several at once.
- Clear All Annotations clears every view mode as one undo; right-click Clear Page Annotations targets the correct page.
- Search waits for a pause in typing before running; the Outlines panel scrolls and no longer auto-expands every branch.
- Pressing Esc during a long OCR, repair, or flatten operation asks whether to cancel instead of closing the window.

## [1.5.1] - 2026-06-14

### Fixed
- PDFs that opened fine in browsers and Acrobat/Foxit but failed in KillerPDF with "Unexpected EOF" now open. PdfSharpCore rejected them during parsing; KillerPDF now falls back to re-saving the file losslessly through PDFium (which reads them) and opening that copy (Issue #72).
- Files opened from UNC / network shares (including the WSL `\\wsl$` filesystem) are now copied to a local temp before opening, avoiding partial-read failures on network filesystems.
- Grid view now renders every page, and tiles stream in progressively as they render instead of blocking until the whole document is done. Grid was previously capped at the first 26 pages, so longer documents stopped loading partway through.
- Ctrl+Scroll in grid view no longer re-renders every page when the zoom is already at its limit (the column count cannot change), which made large documents reload pointlessly.
- Lowered the minimum zoom from 10% to 5% so grid view can pack more columns (useful for wide/landscape pages) and single-page view can zoom out further.
- Removed a stray horizontal scrollbar (a thin green line) that appeared across the bottom of grid view; grid fits its columns to the window and no longer scrolls sideways.

### Changed
- Save Flattened PDF now rasterizes across multiple CPU cores. PNG encoding runs in parallel; the PDFium render step is serialized because the library is not thread-safe. Large documents flatten faster and the UI stays responsive (Issue #68).

## [1.5.0] - 2026-06-14

### Added
- Localization support (Issue #53 / contributor leox243). Language selector in Settings panel. Ships with English (en-US), Spanish (es), and Traditional Chinese (zh-TW). Theme names, zoom dropdown, fit-mode status, and keyboard shortcut overlay all update with the selected language. Contributor guide at `Strings/TRANSLATING.md`.
- Continuous scroll view mode. Opens all pages in a single vertical strip with progressive async rendering. Page number and sidebar thumbnail track automatically as you scroll.
- Two-page view mode. Displays two pages side-by-side (primary + one secondary). Editing tools are available in this mode.
- Re-edit placed text by double-clicking it with the Select tool. The text re-opens with its current content, size, and color; the size dropdown and color swatches restyle it live while editing.
- Per-monitor DPI v2 support. Window and page re-render correctly when dragging between monitors with different scale factors.
- Zoom +/- toolbar buttons and keyboard shortcuts (Ctrl+=, Ctrl+-, Ctrl+0, Ctrl+Scroll).
- Crop tool improvements (Issue #15): editable CropBox coordinates, page range apply, TrimBox sync, rotation-aware coordinate conversion, draggable confirm bar.
- Settings persistence - window size, zoom, and fit mode saved/restored on launch (Issue #69).
- Global crash handler with structured log files and recovery dialog.
- About dialog (click the version label in the status bar).
- Authenticode install gate, downgrade protection, and pdfium.dll integrity check.
- Theme system: Dark, Light, High Contrast, Blood, Greed, and Cyanotic themes with live switching and settings panel (gear icon)
- Grid view zoom fits a whole number of pages across the window. Ctrl+Scroll steps through column counts (3, 4, 5 and up) and the grid opens at three pages across.
- Built-in print dialog with working print preview. Replaces the Windows print dialog (which showed "This app doesn't support print preview") with a themed dialog that previews each page and exposes printer, orientation, copies, and page-range (for example 1-3,5) settings.

### Changed
- Continuous scroll is now the default view mode for new installs.
- View mode order in Settings: Continuous, Single Page, Two-Page, Grid.
- Settings and keyboard shortcut overlay borders widened to 2px for better visibility.
- Text tool size value is now interpreted as points. A size of 14 renders and exports as roughly 14pt instead of about 5pt of internal render units.
- Placing an image now switches to the Select tool with the image selected, so you can immediately drag to reposition or use the corner handle to resize instead of the next click reopening the image picker (matching signature placement).
- Extracted SignatureStore and SearchService into Services/ with unit tests (KillerPDF.Tests).
- Encrypted PDF temp files written to `%LOCALAPPDATA%\KillerPDF\Temp\` instead of `%TEMP%`.
- Reopens last file on startup; ESC closes the app when no overlay is active (Issue #69).
- Grid view mode moved from a toolbar toggle to the Settings panel alongside Theme and Language. Four modes: Single Page, Continuous, Two-Page, Grid. Selection persists across sessions.
- Switching to Single or Two-Page view fits the page to the window, Continuous opens fit-to-width, and Grid opens at its column-fit default, rather than carrying the previous mode's zoom level.
- Annotation toolbars (text and draw size/color) now appear at the top-right under the toolbar buttons instead of the top-left.
- Four corner resize handles on placed images and signatures. Drag any corner to resize with the opposite corner held fixed. Handles are larger and render at the same on-screen size in every view mode.

### Fixed
- Stale debug string appearing in status bar after Fit Width in single-page mode.
- Text edit box closed when changing the font size, because the size dropdown took keyboard focus and triggered a commit. Focus moving into the size or color bar no longer commits the edit.
- Crop confirm bar was scaled down with page zoom, making it unreadable at low zoom levels. Selection rectangle improvements.
- Save Flattened PDF now runs on a background thread (Issue #68).
- Cropped pages rasterize at CropBox size instead of document-wide maximum (Issue #68).
- Temp files cleaned up on close, crash, and startup.
- Undo of a document change (crop, rotate, page operations) now re-renders the active view, so a page no longer keeps showing its pre-undo state while the sidebar shows the correct version.

---

## [1.4.3] - 2026-06-08

### Fixed
- Encrypted PDFs (owner-restricted RC4) no longer fail with "Unexpected token 'xref'" when rotating pages. PdfSharpCore can silently produce a broken cross-reference entry after saving encrypted files; KillerPDF now pipes the file through PDFium to repair the XRef and retries the open automatically.
- Page view now fits to page after a rotation so the full rotated page is visible without manual rezoom.
- Mailto and other link annotations with visible borders (e.g. colored rectangles that looked like strikethroughs) no longer render those borders in saved PDFs. KillerPDF strips `/AP`, `/C`, and `/BS` from link annotations and sets an invisible border on save.
- Right-click a link annotation to remove it from the PDF entirely ("Remove Link from PDF"). Previously, clearing annotations only removed the KillerPDF overlay; the native PDF link remained active.
- Right-click a mailto link to copy just the email address; right-click an http/https link to copy the URL.

---

## [1.4.2] - 2026-06-06

### Added
- PDF form filling. Interactive PDF forms now render their fields (text inputs, checkboxes, radio buttons) as live controls. Fill them in directly and save - field values are written back into the PDF.
- PDF outline (bookmark) support (Issue #63). A new OUTLINES tab in the sidebar displays the document's bookmark tree. Click any entry to jump to that page. The sidebar auto-fits its width to the longest entry on open and can be dragged wider; switching back to PAGES snaps to the pages-mode width.

### Fixed
- Page rotation no longer reverts after saving. Rotations applied via the sidebar context menu now persist correctly through the save pipeline.
- Copied text words were out of order on PDFs where glyphs are stored in non-reading order (Issue #66). Text extraction now sorts words by position and uses a dynamic line-grouping threshold so both drag-select and Select All produce correctly ordered output.
- PDFs with malformed or non-standard XRef tables now open in read-only mode instead of showing "Invalid entry in XRef table" and failing entirely.

---

## [1.4.1] - 2026-05-21

### Added
- Page number jump box in toolbar. Type a page number and press Enter to navigate directly to that page.
- Signature auto-selects after placing so you can immediately reposition or resize without switching tools.
- Zoom to Width / Fit Page now re-applies when the window is resized.
- Middle mouse button panning. Hold middle mouse and drag to pan the view in any direction.
- Multi-page grid view toggle (toolbar button left of the zoom dropdown). Switch between seeing all pages in a scrollable grid and a focused single-page view. Defaults to grid view on open.
- Ctrl+S saves directly to the current file without a dialog. Ctrl+Shift+S opens Save As.
- Arrow key navigation: Left/Up goes to the previous page, Right/Down goes to the next page.
- Keyboard shortcut overlay. Press Ctrl+? to show a full shortcut reference. Dismiss with Escape or by clicking outside the panel.
- Crop tool improvements: corner drag handles to resize the selection after drawing without having to redraw; Enter applies the crop to the current page; Escape cancels; Remove Crop / Remove All buttons in the confirm bar clear an existing CropBox from one page or all pages.

### Fixed
- Fit to Width and Fit Page zoomed incorrectly on HiDPI (4K) displays.
- Pages appeared blurry at higher zoom levels on HiDPI displays.
- Signature position drifted after saving.
- Memory spike (6+ GB) when opening large PDFs on HiDPI displays.
- Navigating pages caused multi-second UI lag on documents with many pages.
- Scroll wheel now navigates to the previous page when scrolled to the top of a page, and to the next page when scrolled to the bottom.

---

## [1.4.0] - 2026-05-16

### Added
- Rotate page (Issue #52). Right-click any page in the sidebar to rotate it 90° clockwise or counter-clockwise. Works on multi-page selections.
- Insert Image tool (Issue #50). Click the toolbar button, then click anywhere on the page to place a PNG, JPG, BMP, GIF, or TIFF as a resizable annotation. Drag the green corner handle to resize; burned into the PDF on save.
- PDF link annotation support (Issue #47). Clicking hyperlinks and internal cross-references in a PDF now navigates to the target page or opens the URL in the default browser. Works on both the primary page and all secondary pages in multi-page grid view.
- New Blank Document (Ctrl+N, toolbar button). Creates a single blank A4 page as a new working document. Prompts to discard unsaved changes if a dirty file is open.
- Typewriter tool font size picker. When the Text tool is active, a settings bar appears showing size presets (8-72pt) and a color palette. Size and color are stored per-annotation and applied when flattening to PDF.
- Insert Blank Page. Right-clicking any page in the sidebar now shows a context menu with page-level operations: insert a blank A4 page, move up/down, extract, or delete.
- Signature resize. Placed signatures now show a green drag handle in the bottom-right corner. Dragging it scales the signature proportionally; releasing commits the new size.
- Multi-page grid view. When viewing a page, subsequent pages render as a tiled grid to the right and below, allowing context across multiple pages at once.
- Fit to Width on open. Files now auto-zoom to fill the viewer width on open instead of opening at 100% and clipping wide pages.

### Fixed
- Scroll wheel in the main viewer no longer triggers page navigation. Previously, at low zoom levels where the page fit entirely in the viewport, every scroll tick caused a full page re-render.
- Page selection no longer flashes centered before jerking left. The layout width is now managed exclusively in the Dispatcher callback, eliminating the double layout pass that caused the visual artifact.
- "Back to TOC" and other internal links on secondary pages now navigate to the correct target instead of advancing to the next sequential page.
- Clicking an internal link now scrolls the viewer back to the top of the target page so links pointing to page tops (e.g. TOC back-links) land correctly.
- Internal PDF links now survive a merge. When merging PDFs, named destinations from the source document's catalog are resolved and rewritten as explicit page-object references in the merged document, so TOC and cross-reference links continue to work after merging.
- Multi-page grid content is now centered in the viewport instead of left-aligned. Panel width is snapped to a whole number of page-width slots so HorizontalAlignment=Center has room to work.
- Sidebar page list no longer shows empty space after the last page. The list now ends at the final page entry with no trailing dead zone.

### Changed
- Theme updated to match killertools.net: accent green changed from `#4ade80` to `#1ea54c`, backgrounds shifted to `#333333`/`#3a3a3a`, sidebar darkened to `#222222`, toolbar and title bar at `#222222`. Film grain overlay added to the main content area. Footer text lightened for readability.
- Sidebar scroll is now handled by an outer ScrollViewer wrapping the page list, allowing the list to size to its content rather than stretching to fill the panel height.

## [1.3.2] - 2026-05-11

### Fixed
- Windows Program Compatibility Assistant popup on first launch. Added an app manifest declaring Windows 10/11 compatibility, which suppresses PCA when the app writes to uninstall registry keys.
- "Set as default PDF viewer" prompt now only appears if KillerPDF is not already the default handler. Previously showed on every install/update regardless.
- "Set as default PDF viewer" prompt now uses the dark KillerDialog instead of a native Windows message box.

## [1.3.1] - 2026-05-11

### Fixed
- Print no longer fails with "No application is associated with the specified file for this action" on systems where Edge is the default PDF handler. Printing now uses WPF-native rendering and PrintDialog instead of the shell print verb.
- Zoom dropdown selected value no longer shows in blue - selection highlight now uses the accent green.

## [1.3.0] - 2026-05-08

### Added
- Image signatures. Import a PNG, JPG, or BMP as a reusable signature instead of drawing one. Stored alongside drawn signatures and flattens into the PDF on save.
- Close File (Ctrl+W). Close the current document without quitting the app. Prompts if there are unsaved changes.
- Unsaved-changes protection. The title bar marks dirty files with `*` and prompts before closing or opening a new file with unsaved edits.
- Full-document Find. Ctrl+F search now scans the entire PDF and cycles through all matches, not just the current page.
- Zoom preset dropdown with quick presets (50%, 75%, 100%, 125%, 150%, 200%). Scroll-wheel zoom syncs the box, including non-preset levels.

### Fixed
- Scrolling past the bottom of a page now advances to the next page; scrolling past the top goes back.
- Re-dropping a PDF onto the window after a file is already open now works correctly.
- Owner-password-protected PDFs now open correctly (previously only user-password was handled).
- Dragging the title bar while maximized now correctly restores and moves the window.
- Delete confirmation now reads "Delete 1 page?" or "Delete 2 pages?" instead of "Delete N page(s)?".
- Signature delete button showed a rectangle glyph instead of an X.

### Changed
- All dialog boxes are now fully dark-themed via a custom dialog window. No more native Windows popups.
- Create Signature dialog now uses a dark custom chrome title bar with a red X close button.
- Button hover states and page thumbnail hover in the sidebar are now green instead of the default Windows blue.
- Toolbar icons overhauled: Open Folder, Close File, Move Up, Move Down, Extract Pages, and Merge PDFs all use cleaner glyphs.

## [1.2.1] - 2026-05-04

### Changed
- Code signed with Certum certificate. Windows now shows a verified publisher instead of unknown.
- Cleaned up footer.

## [1.2.0] - 2026-04-24

### Added
- Self-installing EXE. Running the downloaded binary now shows an Install / Run dialog. Install copies the EXE to `%LOCALAPPDATA%\Programs\KillerPDF\` (no UAC required), creates Start Menu and optional Desktop shortcuts, registers as a PDF file handler, and adds an uninstall entry to Add/Remove Programs. Uninstall self-deletes via a deferred batch file. Running a newer version from outside the install path shows an Update prompt instead.
- Command-line file argument support so file associations work: `KillerPDF.exe "file.pdf"` opens the file directly.
- Password-protected PDF support. Opening an encrypted PDF now prompts for the password instead of showing a generic error. The decrypted copy is held in a temp file for the session so all rendering and editing works normally.
- Save Flattened PDF (photo icon in toolbar). Rasterizes every page at 150 DPI via PDFium and writes them as embedded images into a new PDF, producing a fully uneditable document. Pending annotations are burned in before rasterization.

## [1.1.1] - 2026-04-18

### Fixed
- Maximize no longer covers the Windows taskbar. Added a `WM_GETMINMAXINFO` hook so the frameless window clamps to the monitor's work area (multi-monitor aware).
- Two `CS8602` nullability warnings in the font-name cleanup path.

## [1.1.0] - 2026-04-16

### Changed
- Retargeted from .NET 8 to .NET Framework 4.8 so end users no longer need to install a separate .NET runtime.
- Forced 64-bit build via `PlatformTarget=x64`.
- Added PolySharp polyfills for modern C# language features on net48.
- Replaced `Math.Clamp` calls with `Math.Min`/`Math.Max` equivalents.

### Added
- Post-publish MSBuild target that automatically bundles a GPL3-compliant source zip alongside the published EXE.
- CHANGELOG.md.
- Added hierarchical AcroForm authoring for qualified field names across text, choice, button, and signature fields. Shared nonterminal parents use partial names and deterministic child links, terminal-versus-parent conflicts fail early, selected imports prune omitted branches, and detached signing resolves fully qualified signature fields.
- Attachment filename validation now rejects Unicode control characters and reserved Windows device names independently of the host platform.
