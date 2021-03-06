﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Diagnostics;
using System.Linq;
using ICSharpCode.NRefactory.Utils;
using Mono.Cecil;

namespace ICSharpCode.Decompiler.ILAst
{
	public delegate void PeepholeTransform(ILBlock block, ref int i);
	
	/// <summary>
	/// Handles peephole transformations on the ILAst.
	/// </summary>
	public class PeepholeTransforms
	{
		DecompilerContext context;
		ILBlock method;
		
		public static void Run(DecompilerContext context, ILBlock method)
		{
			PeepholeTransforms transforms = new PeepholeTransforms();
			transforms.context = context;
			transforms.method = method;
			
			PeepholeTransform[] blockTransforms = {
				ArrayInitializers.Transform(method),
				transforms.CachedDelegateInitialization
			};
			Func<ILExpression, ILExpression>[] exprTransforms = {
				EliminateDups,
				HandleDecimalConstants
			};
			// Traverse in post order so that nested blocks are transformed first. This is required so that
			// patterns on the parent block can assume that all nested blocks are already transformed.
			foreach (var node in TreeTraversal.PostOrder<ILNode>(method, c => c != null ? c.GetChildren() : null)) {
				ILBlock block = node as ILBlock;
				ILExpression expr;
				if (block != null) {
					// go through the instructions in reverse so that transforms can build up nested structures inside-out
					for (int i = block.Body.Count - 1; i >= 0; i--) {
						context.CancellationToken.ThrowIfCancellationRequested();
						expr = block.Body[i] as ILExpression;
						if (expr != null) {
							// apply expr transforms to top-level expr in block
							foreach (var t in exprTransforms)
								expr = t(expr);
							block.Body[i] = expr;
						}
						// apply block transforms
						foreach (var t in blockTransforms) {
							t(block, ref i);
							Debug.Assert(i <= block.Body.Count && i >= 0);
							if (i == block.Body.Count) // special case: retry all transforms
								break;
						}
					}
				}
				expr = node as ILExpression;
				if (expr != null) {
					// apply expr transforms to all arguments
					for (int i = 0; i < expr.Arguments.Count; i++) {
						ILExpression arg = expr.Arguments[i];
						foreach (var t in exprTransforms)
							arg = t(arg);
						expr.Arguments[i] = arg;
					}
				}
			}
		}
		
		static ILExpression EliminateDups(ILExpression expr)
		{
			if (expr.Code == ILCode.Dup)
				return expr.Arguments.Single();
			else
				return expr;
		}
		
		#region HandleDecimalConstants
		static ILExpression HandleDecimalConstants(ILExpression expr)
		{
			if (expr.Code == ILCode.Newobj) {
				MethodReference r = (MethodReference)expr.Operand;
				if (r.DeclaringType.Name == "Decimal" && r.DeclaringType.Namespace == "System") {
					if (expr.Arguments.Count == 1) {
						int? val = GetI4Constant(expr.Arguments[0]);
						if (val != null) {
							expr.Arguments.Clear();
							expr.Code = ILCode.Ldc_Decimal;
							expr.Operand = new decimal(val.Value);
							expr.InferredType = r.DeclaringType;
						}
					} else if (expr.Arguments.Count == 5) {
						int? lo = GetI4Constant(expr.Arguments[0]);
						int? mid = GetI4Constant(expr.Arguments[1]);
						int? hi = GetI4Constant(expr.Arguments[2]);
						int? isNegative = GetI4Constant(expr.Arguments[3]);
						int? scale = GetI4Constant(expr.Arguments[4]);
						if (lo != null && mid != null && hi != null && isNegative != null && scale != null) {
							expr.Arguments.Clear();
							expr.Code = ILCode.Ldc_Decimal;
							expr.Operand = new decimal(lo.Value, mid.Value, hi.Value, isNegative.Value != 0, (byte)scale);
							expr.InferredType = r.DeclaringType;
						}
					}
				}
			}
			return expr;
		}
		
		static int? GetI4Constant(ILExpression expr)
		{
			if (expr != null && expr.Code == ILCode.Ldc_I4)
				return (int)expr.Operand;
			else
				return null;
		}
		#endregion
		
		#region CachedDelegateInitialization
		void CachedDelegateInitialization(ILBlock block, ref int i)
		{
			// if (logicnot(ldsfld(field))) {
			//     stsfld(field, newobj(Action::.ctor, ldnull(), ldftn(method)))
			// } else {
			// }
			// ...(..., ldsfld(field), ...)
			
			ILCondition c = block.Body[i] as ILCondition;
			if (c == null || c.Condition == null && c.TrueBlock == null || c.FalseBlock == null)
				return;
			if (!(c.TrueBlock.Body.Count == 1 && c.FalseBlock.Body.Count == 0))
				return;
			if (!c.Condition.Match(ILCode.LogicNot))
				return;
			ILExpression condition = c.Condition.Arguments.Single() as ILExpression;
			if (condition == null || condition.Code != ILCode.Ldsfld)
				return;
			FieldDefinition field = condition.Operand as FieldDefinition; // field is defined in current assembly
			if (field == null || !field.IsCompilerGeneratedOrIsInCompilerGeneratedClass())
				return;
			ILExpression stsfld = c.TrueBlock.Body[0] as ILExpression;
			if (!(stsfld != null && stsfld.Code == ILCode.Stsfld && stsfld.Operand == field))
				return;
			ILExpression newObj = stsfld.Arguments[0];
			if (!(newObj.Code == ILCode.Newobj && newObj.Arguments.Count == 2))
				return;
			if (newObj.Arguments[0].Code != ILCode.Ldnull)
				return;
			if (newObj.Arguments[1].Code != ILCode.Ldftn)
				return;
			MethodDefinition anonymousMethod = newObj.Arguments[1].Operand as MethodDefinition; // method is defined in current assembly
			if (!Ast.Transforms.DelegateConstruction.IsAnonymousMethod(context, anonymousMethod))
				return;
			
			ILExpression expr = block.Body.ElementAtOrDefault(i + 1) as ILExpression;
			if (expr != null && expr.GetSelfAndChildrenRecursive<ILExpression>().Count(e => e.Code == ILCode.Ldsfld && e.Operand == field) == 1) {
				foreach (ILExpression parent in expr.GetSelfAndChildrenRecursive<ILExpression>()) {
					for (int j = 0; j < parent.Arguments.Count; j++) {
						if (parent.Arguments[j].Code == ILCode.Ldsfld && parent.Arguments[j].Operand == field) {
							parent.Arguments[j] = newObj;
							block.Body.RemoveAt(i);
							i -= ILInlining.InlineInto(block, i, method);
							return;
						}
					}
				}
			}
		}
		#endregion
	}
}
