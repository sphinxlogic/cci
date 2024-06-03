﻿using System;
using System.Collections.Generic;
using System.Text;
using CciSharp.Framework;
using Microsoft.Cci;
using Microsoft.Cci.Contracts;
using Microsoft.Cci.MutableCodeModel;
using Microsoft.Cci.MutableContracts;

namespace CciSharp.Mutators
{
    /// <summary>
    /// A mutator that injects non-null postconditions
    /// in visible methods
    /// </summary>
    public sealed class EnsuresNotNullMutator
      :CcsMutatorBase
    {

        public EnsuresNotNullMutator(ICcsHost host)
            : base(host,"Ensures Not Null",0,typeof(EnsuresNotNullMutator))
        {
        }

        public override bool Visit()
        {
            var assembly = this.Host.MutatedAssembly;
            PdbReader _pdbReader;
            if(!this.Host.TryGetMutatedPdbReader(out _pdbReader))
                _pdbReader = null;
            var contracts = this.Host.MutatedContracts;

            new NonNullInjector(this.Host,contracts).Traverse(this.Host.MutatedAssembly);
            ContractHelper.InjectContractCalls(this.Host,this.Host.MutatedAssembly,contracts,_pdbReader);

            return true;
        }

        sealed class NonNullInjector
          : CodeTraverser
        {
            readonly ICcsHost host;
            readonly ContractProvider contractProvider;

            public NonNullInjector(
              ICcsHost host,
              ContractProvider contractProvider)
            {
                this.host = host;
                this.contractProvider = contractProvider;
            }

            public override void TraverseChildren(IMethodDefinition method)
            {
                // inject only in visible method
                if(!MemberHelper.IsVisibleOutsideAssembly(method))
                    return;

                // inject only in methods that return a reference type
                var returnType = method.Type;
                if(returnType == this.host.PlatformType.SystemVoid
                  || returnType.IsValueType)
                    return;

                // create new postcondition
                // Contract.Ensures(Contract.Result<T>() != null);
                var newContract = new MethodContract
                {
                    Postconditions = new List<IPostcondition> {
                        new Postcondition {
                          // !=
                          Condition = new NotEquality {
                            // result
                            LeftOperand = new ReturnValue { Type = returnType, },
                            // null
                            RightOperand = new CompileTimeConstant {
                              Type = returnType,
                              Value = null,
                            },
                            Type = this.host.PlatformType.SystemBoolean,
                          },
                          // description
                          OriginalSource = "result != null",
                        }
                      }
                };

                // merge existing contracts, if any
                var contract = this.contractProvider.GetMethodContractFor(method);
                if(contract != null)
                {
                    ContractHelper.AddMethodContract(newContract,contract);
                }
                // store new contracts
                this.host.Event(CcsEventLevel.Message,"ensures not null: {0}",method);
                this.contractProvider.AssociateMethodWithContract(method,newContract);
            }
        }
    }
}
