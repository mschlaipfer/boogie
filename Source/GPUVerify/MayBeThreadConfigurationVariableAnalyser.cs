﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Microsoft.Boogie;

namespace GPUVerify
{
    class MayBeThreadConfigurationVariableAnalyser
    {

        private GPUVerifier verifier;

        private bool ProcedureChanged;

        private Dictionary<string, Dictionary<string, bool>> mayBeLocalXInfo;
        private Dictionary<string, Dictionary<string, bool>> mayBeLocalYInfo;
        private Dictionary<string, Dictionary<string, bool>> mayBeLocalZInfo;

        public MayBeThreadConfigurationVariableAnalyser(GPUVerifier verifier)
        {
            this.verifier = verifier;
            mayBeLocalXInfo = new Dictionary<string, Dictionary<string, bool>>();
            mayBeLocalYInfo = new Dictionary<string, Dictionary<string, bool>>();
            mayBeLocalZInfo = new Dictionary<string, Dictionary<string, bool>>();
        }

        internal void Analyse()
        {
            foreach (Declaration D in verifier.Program.TopLevelDeclarations)
            {
                if(D is Implementation)
                {
                    Implementation Impl = D as Implementation;
                    mayBeLocalXInfo.Add(Impl.Name, new Dictionary<string, bool> ());
                    mayBeLocalYInfo.Add(Impl.Name, new Dictionary<string, bool>());
                    mayBeLocalZInfo.Add(Impl.Name, new Dictionary<string, bool>());

                    SetMayBeLocal("X", Impl.Name, GPUVerifier._X.Name);
                    SetNotLocal("X", Impl.Name, GPUVerifier._Y.Name);
                    SetNotLocal("X", Impl.Name, GPUVerifier._Z.Name);

                    SetNotLocal("Y", Impl.Name, GPUVerifier._X.Name);
                    SetMayBeLocal("Y", Impl.Name, GPUVerifier._Y.Name);
                    SetNotLocal("Y", Impl.Name, GPUVerifier._Z.Name);

                    SetNotLocal("Z", Impl.Name, GPUVerifier._X.Name);
                    SetNotLocal("Z", Impl.Name, GPUVerifier._Y.Name);
                    SetMayBeLocal("Z", Impl.Name, GPUVerifier._Z.Name);

                    foreach (Variable v in Impl.LocVars)
                    {
                        SetMayBeLocal("X", Impl.Name, v.Name);
                        SetMayBeLocal("Y", Impl.Name, v.Name);
                        SetMayBeLocal("Z", Impl.Name, v.Name);
                    }

                    foreach (Variable v in Impl.InParams)
                    {
                        SetMayBeLocal("X", Impl.Name, v.Name);
                        SetMayBeLocal("Y", Impl.Name, v.Name);
                        SetMayBeLocal("Z", Impl.Name, v.Name);
                    }

                    foreach (Variable v in Impl.OutParams)
                    {
                        SetMayBeLocal("X", Impl.Name, v.Name);
                        SetMayBeLocal("Y", Impl.Name, v.Name);
                        SetMayBeLocal("Z", Impl.Name, v.Name);
                    }

                    ProcedureChanged = true;
                }
            }

            while (ProcedureChanged)
            {
                ProcedureChanged = false;

                foreach (Declaration D in verifier.Program.TopLevelDeclarations)
                {
                    if (D is Implementation)
                    {
                        Implementation Impl = D as Implementation;
                        Analyse(Impl);
                    }
                }
            }

            if (CommandLineOptions.ShowMayBeThreadConfigurationVariableAnalysis)
            {
                dump();
            }
        }

        private void Analyse(Implementation Impl)
        {
            Analyse(Impl, Impl.StructuredStmts);
        }

        private void Analyse(Implementation impl, StmtList stmtList)
        {
            foreach (BigBlock bb in stmtList.BigBlocks)
            {
                Analyse(impl, bb);
            }
        }

        private void Analyse(Implementation impl, BigBlock bb)
        {
            foreach (Cmd c in bb.simpleCmds)
            {
                if (c is AssignCmd)
                {
                    TransferAssign(impl, c as AssignCmd, "X");
                    TransferAssign(impl, c as AssignCmd, "Y");
                    TransferAssign(impl, c as AssignCmd, "Z");
                }
                else if (c is CallCmd)
                {
                    TransferCall(impl, c as CallCmd, "X");
                    TransferCall(impl, c as CallCmd, "Y");
                    TransferCall(impl, c as CallCmd, "Z");
                }
                else if (c is HavocCmd)
                {
                    TransferHavoc(impl, c as HavocCmd, "X");
                    TransferHavoc(impl, c as HavocCmd, "Y");
                    TransferHavoc(impl, c as HavocCmd, "Z");
                }
            }

            if (bb.ec is WhileCmd)
            {
                WhileCmd wc = bb.ec as WhileCmd;
                Analyse(impl, wc.Body);
            }
            else if (bb.ec is IfCmd)
            {
                IfCmd ifCmd = bb.ec as IfCmd;
                Analyse(impl, ifCmd.thn);
                if (ifCmd.elseBlock != null)
                {
                    Analyse(impl, ifCmd.elseBlock);
                }
                Debug.Assert(ifCmd.elseIf == null);
            }

        }

        private void TransferHavoc(Implementation impl, HavocCmd havoc, string dim)
        {
            Debug.Assert(havoc.Vars.Length == 1);
            if (MayBeLocal(dim, impl.Name, havoc.Vars[0].Decl.Name))
            {
                SetNotLocal(dim, impl.Name, havoc.Vars[0].Decl.Name);
            }
        }

        private void TransferCall(Implementation impl, CallCmd callCmd, string dim)
        {
            if (callCmd.callee != verifier.BarrierProcedure.Name)
            {

                Implementation CalleeImplementation = verifier.GetImplementation(callCmd.callee);
                for (int i = 0; i < CalleeImplementation.InParams.Length; i++)
                {
                    if (MayBeLocal(dim, callCmd.callee, CalleeImplementation.InParams[i].Name)
                        && !MayBeLocal(dim, impl.Name, callCmd.Ins[i]))
                    {
                        SetNotLocal(dim, callCmd.callee, CalleeImplementation.InParams[i].Name);
                    }
                }

                for (int i = 0; i < CalleeImplementation.OutParams.Length; i++)
                {
                    if (MayBeLocal(dim, impl.Name, callCmd.Outs[i].Name)
                        && !MayBeLocal(dim, callCmd.callee, CalleeImplementation.OutParams[i].Name))
                    {
                        SetNotLocal(dim, impl.Name, callCmd.Outs[i].Name);
                    }
                }

            }
        }

        private void TransferAssign(Implementation impl, AssignCmd assignCmd, string dim)
        {
            Debug.Assert(assignCmd.Lhss.Count == 1);
            Debug.Assert(assignCmd.Rhss.Count == 1);
            if (assignCmd.Lhss[0] is SimpleAssignLhs)
            {
                SimpleAssignLhs lhs = assignCmd.Lhss[0] as SimpleAssignLhs;
                Expr rhs = assignCmd.Rhss[0];

                if (MayBeLocal(dim, impl.Name, lhs.AssignedVariable.Name)
                    && !MayBeLocal(dim, impl.Name, rhs))
                {
                    SetNotLocal(dim, impl.Name, lhs.AssignedVariable.Name);
                }

            }
        }

        private void SetNotLocal(string dim, string proc, string v)
        {
            GetMayBeLocalDimInfo(dim)[proc][v] = false;
            ProcedureChanged = true;
        }

        private void SetMayBeLocal(string dim, string proc, string v)
        {
            GetMayBeLocalDimInfo(dim)[proc][v] = true;
        }

        internal bool MayBeLocal(string dim, string proc, string v)
        {
            if (!GetMayBeLocalDimInfo(dim).ContainsKey(proc))
            {
                return false;
            }

            if (!GetMayBeLocalDimInfo(dim)[proc].ContainsKey(v))
            {
                return false;
            }

            return GetMayBeLocalDimInfo(dim)[proc][v];
        }

        internal bool MayBeLocal(string dim, string proc, Expr e)
        {
            if (e is IdentifierExpr)
            {
                return MayBeLocal(dim, proc, (e as IdentifierExpr).Decl.Name);
            }
            return false;
        }

        private void dump()
        {
            foreach (string p in mayBeLocalXInfo.Keys)
            {
                Console.WriteLine("Procedure " + p);
                foreach (string v in mayBeLocalXInfo[p].Keys)
                {
                    if (mayBeLocalXInfo[p][v])
                    {
                        Console.WriteLine("  " + v + ": may be " + GPUVerifier.LOCAL_ID_X_STRING);
                    }
                }
                foreach (string v in mayBeLocalYInfo[p].Keys)
                {
                    if (mayBeLocalYInfo[p][v])
                    {
                        Console.WriteLine("  " + v + ": may be " + GPUVerifier.LOCAL_ID_Y_STRING);
                    }
                }
                foreach (string v in mayBeLocalZInfo[p].Keys)
                {
                    if (mayBeLocalZInfo[p][v])
                    {
                        Console.WriteLine("  " + v + ": may be " + GPUVerifier.LOCAL_ID_Z_STRING);
                    }
                }
            }

        }

        private Dictionary<string, Dictionary<string, bool>> GetMayBeLocalDimInfo(string dim)
        {
            Dictionary<string, Dictionary<string, bool>> map = null;
            if (dim.Equals("X"))
            {
                map = mayBeLocalXInfo;
            }
            else if (dim.Equals("Y"))
            {
                map = mayBeLocalYInfo;
            }
            else if (dim.Equals("Z"))
            {
                map = mayBeLocalZInfo;
            }
            else
            {
                Debug.Assert(false);
            }
            return map;
        }

    }
}