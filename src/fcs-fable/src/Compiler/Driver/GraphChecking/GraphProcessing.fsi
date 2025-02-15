/// Parallel processing of graph of work items with dependencies
module internal FSharp.Compiler.GraphChecking.GraphProcessing

open System.Threading

/// Information about the node in a graph, describing its relation with other nodes.
type NodeInfo<'Item> =
    { Item: 'Item
      Deps: 'Item[]
      TransitiveDeps: 'Item[]
      Dependents: 'Item[] }

/// An already processed node in the graph, with its result available
type ProcessedNode<'Item, 'Result> =
    { Info: NodeInfo<'Item>
      Result: 'Result }

type GraphProcessingException =
    inherit exn
    new: msg: string * ex: System.Exception -> GraphProcessingException

/// <summary>
/// A generic method to generate results for a graph of work items in parallel.
/// Processes leaves first, and after each node has been processed, schedules any now unblocked dependents.
/// Returns a list of results, one per item.
/// Uses the Thread Pool to schedule work.
/// </summary>
/// <param name="graph">Graph of work items</param>
/// <param name="work">A function to generate results for a single item</param>
/// <param name="parentCt">Cancellation token</param>
/// <remarks>
/// An alternative scheduling approach is to schedule N parallel tasks that process items from a BlockingCollection.
/// My basic tests suggested it's faster, although confirming that would require more detailed testing.
/// </remarks>
val processGraph<'Item, 'Result when 'Item: equality and 'Item: comparison> :
    graph: Graph<'Item> ->
    work: (('Item -> ProcessedNode<'Item, 'Result>) -> NodeInfo<'Item> -> 'Result) ->
    parentCt: CancellationToken ->
        ('Item * 'Result)[]

#if !FABLE_COMPILER
val processGraphAsync<'Item, 'Result when 'Item: equality and 'Item: comparison> :
    graph: Graph<'Item> ->
    work: (('Item -> ProcessedNode<'Item, 'Result>) -> NodeInfo<'Item> -> Async<'Result>) ->
        Async<('Item * 'Result)[]>
#endif //!FABLE_COMPILER
