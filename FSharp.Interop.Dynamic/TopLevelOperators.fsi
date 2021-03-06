namespace FSharp.Interop.Dynamic
  [<AutoOpen>]
  module TopLevelOperators = begin
    val ( ? ) : target:obj -> name:string -> 'TResult
    val ( ?<- ) : target:obj -> name:string -> value:'TValue -> unit
    val ( !? ) : target:obj -> 'TResult
  end

