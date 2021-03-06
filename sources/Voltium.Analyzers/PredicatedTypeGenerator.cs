using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Voltium.Analyzers
{
    internal abstract class PredicatedTypeGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            // if you wanna debug this method, uncomment this
            // ugly but works. blame roslyn devs not me

            var receiver = (SyntaxTypeReceiver<TypeDeclarationSyntax>)context.SyntaxReceiver!;

            var nodes = receiver.SyntaxNodes;
            var comp = context.Compilation;

            OnExecute(context);

            // this handles partial types, which have multiple type declaration nodes
            var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var node in nodes)
            {
                var tree = node.SyntaxTree;
                var semantics = comp.GetSemanticModel(tree);
                var type = (INamedTypeSymbol)semantics.GetDeclaredSymbol(node)!;

                if (!Predicate(context, type) || visited.Contains(type))
                {
                    continue;
                }

                GenerateFromSyntax(context, node, tree);
                GenerateFromSymbol(context, type);
                visited.Add(type);
            }
        }

        protected virtual void OnExecute(GeneratorExecutionContext context) { }

        protected abstract bool Predicate(GeneratorExecutionContext context, INamedTypeSymbol decl);


        protected virtual void GenerateFromSyntax(GeneratorExecutionContext context, TypeDeclarationSyntax syntax, SyntaxTree tree) { }
        protected virtual void GenerateFromSymbol(GeneratorExecutionContext context, INamedTypeSymbol symbol) {  }

        public void Initialize(GeneratorInitializationContext context)
            => context.RegisterForSyntaxNotifications(() => new SyntaxTypeReceiver<TypeDeclarationSyntax>());
    }

    internal abstract class AttributedTypeGenerator : PredicatedTypeGenerator
    {
        protected abstract string AttributeName { get; }

        protected sealed override bool Predicate(GeneratorExecutionContext context, INamedTypeSymbol decl)
        {
            if (decl.Name.Contains("Debug"))
            {
            }
            return decl.HasAttribute(AttributeName, context.Compilation);
        }
    }


    internal abstract class PredicatedGenerator<T> : ISourceGenerator where T : SyntaxNode
    {
        public void Execute(GeneratorExecutionContext context)
        {
            // if you wanna debug this method, uncomment this
            // ugly but works. blame roslyn devs not me

            var receiver = (SyntaxTypeReceiver<T>)context.SyntaxReceiver!;

            var nodes = receiver.SyntaxNodes;

            var comp = context.Compilation;

            OnExecute(context);

            // this handles partial types, which have multiple type declaration nodes
            var visited = new HashSet<ISymbol>();
            foreach (var node in nodes)
            {
                var semantics = comp.GetSemanticModel(node.SyntaxTree);
                var symbol = semantics.GetDeclaredSymbol(node)!;

                Helpers.Assert(symbol is not null);

                if (!Predicate(context, symbol) || visited.Contains(symbol))
                {
                    continue;
                }

                Generate(context, symbol, node);
                visited.Add(symbol);
            }
        }

        protected virtual void OnExecute(GeneratorExecutionContext context) { }

        protected abstract bool Predicate(GeneratorExecutionContext context, ISymbol decl);

        protected abstract void Generate(GeneratorExecutionContext context, ISymbol symbol, T syntax);

        public void Initialize(GeneratorInitializationContext context)
            => context.RegisterForSyntaxNotifications(() => new SyntaxTypeReceiver<T>());
    }
}
