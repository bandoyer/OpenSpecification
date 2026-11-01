# Query builder

A developer builds a specification through its `Query` property, which `Specification<T>` and `Specification<T, TResult>` have, and the single-result classes inherit. Since #8, neither `ISpecification<T>` nor `ISpecification<T, TResult>` declares `Query`. Reading it through either interface fails to build, a specification class of the developer's own implements `ISpecification<T>` without it, and an explicit implementation of it fails to build. Each class's `Query` has an XML summary of its own, which the IDE shows on hover.

## Sub-features

- `query-on-classes`: the consumer's specs read `Query` through `Specification<T>`, `Specification<T, TResult>`, `SingleResultSpecification<T>` and `SingleResultSpecification<T, TResult>`, and build.
- `query-through-interface`: reading `Query` through `ISpecification<Company>` or `ISpecification<Company, string>` fails with CS1061.
- `specification-without-query`: a class that implements `ISpecification<Company>` with every member except `Query` builds.
- `explicit-query`: implementing `Query` explicitly, as `ISpecification<Company>.Query`, fails with CS0539.
- `query-docs`: the core library's XML docs give `Specification<T>.Query` and `Specification<T, TResult>.Query` a `<summary>` each.

## How to get to it (user POV)

- Write a specification class and call `Query` in its constructor, or read `Query` from a variable of one of the interface types.
- Write a class that implements `ISpecification<T>` directly, and build.
- Hover `Query` in the IDE, which shows the summary from the library's XML docs.

## Driving it with verify

Preconditions:

- `verify doctor` passes.
- `T` is a tree from `verify tree <commit>`, and `S` is `.agents/skills/verify-specification/consumer-snippets`.

- **Classes.** Run `C=$(verify consumer "$T")`, then `verify build "$C"`. It ends `build: exit 0`, since `Program.cs` reads `Query` through each of the four classes. [EF6 repository queries](./ef6-repository-queries.md) runs the queries those specs build.
- **Snippets.** For each file in `$S`, make a consumer of its own and copy the file in, then build it with its errors:

  ```bash
  C=$(verify consumer "$T" "$(basename "$T")-interface")
  sed "s/\bOpenSpecification\b/$(verify library "$T")/g" "$S/QueryThroughInterface.cs" > "$C/QueryThroughInterface.cs"
  verify build "$C" 2>&1
  ```

  Since #8, the three files give these results:

  | File | Since #8 | Before #8 |
  |---|---|---|
  | `QueryThroughInterface.cs` | `build: exit 1`, then `error CS1061` on each line marked `QUERY` | `build: exit 0` |
  | `SpecificationWithoutQuery.cs` | `build: exit 0` | `build: exit 1`, then `error CS0535` for `ISpecification<Company>.Query` |
  | `ExplicitQuery.cs` | `build: exit 1`, then `error CS0539` on the line marked `QUERY` | `build: exit 0` |

- **Docs.** After a `verify build "$C"` that ends `build: exit 0`, such as the Classes step's, print the two members' entries from the core library's XML docs:

  ```bash
  L=$(verify library "$T")
  sed -n "/name=\"P:$L.Specification\`[12].Query\"/,/<\/member>/p" "$C/bin/Debug/net10.0/$L.xml"
  ```

  Since #8, each of the two entries holds a `<summary>`. Before #8, each holds only `<inheritdoc/>`, which has no interface member left to inherit from once `Query` leaves the interfaces.

## Gotchas

- `verify build` prints the errors on stderr and the rest on stdout, so save both with `2>&1`.
- The snippets import `OpenSpecification`. The `sed` in their recipe writes the tree's library name in its place, which is `Ardalis.Specification` for every tree from before #8, since #47 came later.
- Each snippet goes in a consumer of its own. A failing snippet stops the whole consumer from building, which would hide another snippet's result.
- An error's line number is the snippet file's, such as `QueryThroughInterface.cs(7,...)`, since the consumer compiles the copy in place.
