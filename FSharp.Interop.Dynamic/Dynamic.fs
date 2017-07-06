﻿//
//  Copyright 2011  Ekon Benefits
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

namespace FSharp.Interop.Dynamic

[<AutoOpen>]
module TopLevelOperators=
    open System
    open Microsoft.CSharp.RuntimeBinder
    open Microsoft.FSharp.Reflection
    open Dynamitey

    ///Dynamic get property or method invocation
    let (?)  (target : obj) (name:string)  : 'TResult =
        let resultType = typeof<'TResult>
        let (|NoConversion| Conversion|) t = if t = typeof<obj> then NoConversion else Conversion

        if not (FSharpType.IsFunction resultType)
        then
            let convert r = match resultType with
                                | NoConversion -> r
                                | ____________ -> Dyn.implicitConvert r

            Dynamic.InvokeGet(target, name)
                |> convert
                |> unbox
        else
            let lambda = fun arg ->
                               let argType,returnType = FSharpType.GetFunctionElements resultType

                               let argArray =
                                    match argType with
                                    | a when FSharpType.IsTuple(a) -> FSharpValue.GetTupleFields(arg)
                                    | a when a = typeof<unit>      -> [| |]
                                    | ____________________________ -> [|arg|]

                               let invoker k = Invocation(k, InvokeMemberName(name ,null)).Invoke(target, argArray)

                               let (|Action|Func|) t = if t = typeof<unit> then Action else Func
                               let (|Invoke|InvokeMember|) n = if n = "_" then Invoke else InvokeMember

                               let result =
                                    try //Either it has a member or it's something directly callable
                                        match (returnType, name) with
                                        | (Action,Invoke) -> invoker(InvocationKind.InvokeAction)
                                        | (Action,InvokeMember) -> invoker(InvocationKind.InvokeMemberAction)
                                        | (Func, Invoke) -> invoker(InvocationKind.Invoke)
                                        | (Func, InvokeMember) -> invoker(InvocationKind.InvokeMember)
                                    with  //Last chance incase we are trying to invoke an fsharpfunc
                                        |  :? RuntimeBinderException as e  ->
                                            try
                                                let invokeName =InvokeMemberName("Invoke", null) //FSharpFunc Invoke
                                                let invokeContext t = InvokeContext(t,typeof<obj>) //Improve cache hits by using the same context
                                                let invokeFSharpFold t (a:obj) =
                                                    Dynamic.InvokeMember(invokeContext(t),invokeName,a)
                                                let seed = match name with
                                                           |InvokeMember -> Dynamic.InvokeGet(target,name)
                                                           |Invoke       -> target
                                                Array.fold invokeFSharpFold seed argArray
                                            with
                                            #if SILVERLIGHT
                                                | :? RuntimeBinderException
                                                     -> raise e
                                            #else
                                                | :? RuntimeBinderException as e2
                                                     -> AggregateException(e,e2) |> raise
                                            #endif

                               match returnType with
                               | Action | NoConversion -> result
                               | _____________________ -> Dyn.implicitConvertTo returnType result

            FSharpValue.MakeFunction(resultType,lambda) |> unbox<'TResult>

    ///Dynamic set property
    let (?<-) (target : obj) (name : string) (value : 'TValue) : unit =
        Dynamic.InvokeSet(target, name, value) |> ignore

    ///Prefix operator that allows direct dynamic invocation of the object
    let (!?) (target:obj) : 'TResult =
        target?``_``
