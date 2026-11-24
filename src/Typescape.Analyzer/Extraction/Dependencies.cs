using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>
/// The Dependencies one document's syntax shows, one fact per place: from the innermost own type around it to the
/// named type's open definition. A type's use of itself is no Dependency.
/// </summary>
internal sealed class Dependencies : CSharpSyntaxWalker
{
    private readonly SemanticModel model;
    private readonly string document;
    private readonly ProjectScope scope;
    private readonly List<DependencyFact> facts = [];
    private (INamedTypeSymbol Type, string Key)? source;
    private ISymbol? member;

    private Dependencies(SemanticModel model, string document, ProjectScope scope)
    {
        this.model = model;
        this.document = document;
        this.scope = scope;
    }

    /// <param name="document">The key of the document <paramref name="model"/> is for.</param>
    public static IReadOnlyList<DependencyFact> Of(SemanticModel model, string document, ProjectScope scope)
    {
        var walker = new Dependencies(model, document, scope);
        walker.Visit(model.SyntaxTree.GetRoot(scope.CancellationToken));
        return walker.facts;
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node) => InType(node, () => base.VisitClassDeclaration(node));

    public override void VisitStructDeclaration(StructDeclarationSyntax node) => InType(node, () => base.VisitStructDeclaration(node));

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => InType(node, () => base.VisitInterfaceDeclaration(node));

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node) => InType(node, () => base.VisitRecordDeclaration(node));

    // An enum's base is its underlying integral type, not a base type.
    public override void VisitEnumDeclaration(EnumDeclarationSyntax node) => InType(node, () => base.VisitEnumDeclaration(node));

    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node) => InType(node, () => base.VisitDelegateDeclaration(node));

    // No own type: the receiver is part of the signature of every member, which belong to the static class.
    public override void VisitExtensionBlockDeclaration(ExtensionBlockDeclarationSyntax node)
    {
        Parameters(node.ParameterList);
        base.VisitExtensionBlockDeclaration(node);
    }

    public override void VisitGlobalStatement(GlobalStatementSyntax node)
    {
        if (model.GetDeclaredSymbol((CompilationUnitSyntax)node.Parent!, scope.CancellationToken) is { ContainingType: { } program })
        {
            InSource(program, () => base.VisitGlobalStatement(node));
        }
        else
        {
            base.VisitGlobalStatement(node);
        }
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node) =>
        InMember(node.Declaration.Variables[0], () =>
        {
            Use(node.Declaration.Type, DependencyKind.MemberType);
            base.VisitFieldDeclaration(node);
        });

    public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node) =>
        InMember(node.Declaration.Variables[0], () =>
        {
            Use(node.Declaration.Type, DependencyKind.MemberType);
            base.VisitEventFieldDeclaration(node);
        });

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Use(node.Type, DependencyKind.MemberType);
            base.VisitPropertyDeclaration(node);
        });

    public override void VisitEventDeclaration(EventDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Use(node.Type, DependencyKind.MemberType);
            base.VisitEventDeclaration(node);
        });

    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Use(node.Type, DependencyKind.MemberType);
            Parameters(node.ParameterList);
            base.VisitIndexerDeclaration(node);
        });

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Use(node.ReturnType, DependencyKind.Signature);
            Parameters(node.ParameterList);
            base.VisitMethodDeclaration(node);
        });

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Parameters(node.ParameterList);
            base.VisitConstructorDeclaration(node);
        });

    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node) =>
        InMember(node, () => base.VisitDestructorDeclaration(node));

    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Use(node.ReturnType, DependencyKind.Signature);
            Parameters(node.ParameterList);
            base.VisitOperatorDeclaration(node);
        });

    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Use(node.Type, DependencyKind.Signature);
            Parameters(node.ParameterList);
            base.VisitConversionOperatorDeclaration(node);
        });

    public override void VisitAccessorDeclaration(AccessorDeclarationSyntax node) =>
        InMember(node, () => base.VisitAccessorDeclaration(node));

    // The implemented interface is part of the member's signature, as Executable Member signatures show it.
    public override void VisitExplicitInterfaceSpecifier(ExplicitInterfaceSpecifierSyntax node)
    {
        Use(node.Name, DependencyKind.Signature);
        base.VisitExplicitInterfaceSpecifier(node);
    }

    // A constraint is part of the generic declaration's signature. A local function's belongs to its body.
    public override void VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax node)
    {
        if (node.Parent is not LocalFunctionStatementSyntax)
        {
            foreach (var constraint in node.Constraints.OfType<TypeConstraintSyntax>())
            {
                Use(constraint.Type, DependencyKind.Signature);
            }
        }

        base.VisitTypeParameterConstraintClause(node);
    }

    public override void VisitAttribute(AttributeSyntax node)
    {
        // A constructor that fails overload resolution still names its attribute type among the candidates.
        var info = model.GetSymbolInfo(node, scope.CancellationToken);
        Record((info.Symbol ?? info.CandidateSymbols.FirstOrDefault())?.ContainingType, DependencyKind.Attribute, node);
        base.VisitAttribute(node);
    }

    // Wherever the list is, in a declaration or a body; a nested list is visited on its own.
    public override void VisitTypeArgumentList(TypeArgumentListSyntax node)
    {
        foreach (var argument in node.Arguments)
        {
            Use(argument, DependencyKind.GenericArg);
        }

        base.VisitTypeArgumentList(node);
    }

    private void InType(MemberDeclarationSyntax node, Action visitChildren)
    {
        if (model.GetDeclaredSymbol(node, scope.CancellationToken) is not INamedTypeSymbol type)
        {
            visitChildren();
            return;
        }

        InSource(type, () =>
        {
            switch (node)
            {
                case TypeDeclarationSyntax declaration:
                    foreach (var baseType in declaration.BaseList?.Types ?? [])
                    {
                        Use(baseType.Type, BaseKind(type, baseType.Type));
                    }

                    Parameters(declaration.ParameterList);
                    PositionalProperties(type, declaration.ParameterList);
                    break;
                case DelegateDeclarationSyntax @delegate:
                    Use(@delegate.ReturnType, DependencyKind.Signature);
                    Parameters(@delegate.ParameterList);
                    break;
            }

            visitChildren();
        });
    }

    private void InSource(INamedTypeSymbol type, Action visit)
    {
        var outer = (source, member);
        (source, member) = ((type, Symbols.TypeKey(type, scope.Context)), null);
        visit();
        (source, member) = outer;
    }

    private void InMember(SyntaxNode declaration, Action visit)
    {
        var outer = member;
        member = model.GetDeclaredSymbol(declaration, scope.CancellationToken) ?? outer;
        visit();
        member = outer;
    }

    private DependencyKind BaseKind(INamedTypeSymbol type, TypeSyntax baseType) =>
        type.TypeKind != TypeKind.Interface && model.GetTypeInfo(baseType, scope.CancellationToken).Type is { TypeKind: TypeKind.Interface }
            ? DependencyKind.Implements
            : DependencyKind.Inherits;

    private void Parameters(BaseParameterListSyntax? parameters)
    {
        foreach (var parameter in parameters?.Parameters ?? [])
        {
            if (parameter.Type is { } type)
            {
                Use(type, DependencyKind.Signature);
            }
        }
    }

    // A record's positional parameter also declares a property, unless the record declares one of that name itself.
    private void PositionalProperties(INamedTypeSymbol record, ParameterListSyntax? parameters)
    {
        foreach (var parameter in record.IsRecord ? parameters?.Parameters ?? [] : [])
        {
            var declaresProperty = record.GetMembers(parameter.Identifier.ValueText).OfType<IPropertySymbol>()
                .Any(property => property.DeclaringSyntaxReferences.Any(reference => reference.Span == parameter.Span && reference.SyntaxTree == parameter.SyntaxTree));
            if (declaresProperty && parameter.Type is { } type)
            {
                Use(type, DependencyKind.MemberType);
            }
        }
    }

    // Syntax sugar is see-through: the code names the element types, not Nullable<T> or ValueTuple.
    private void Use(TypeSyntax syntax, DependencyKind kind)
    {
        switch (syntax)
        {
            case ArrayTypeSyntax array:
                Use(array.ElementType, kind);
                break;
            case NullableTypeSyntax nullable:
                Use(nullable.ElementType, kind);
                break;
            case PointerTypeSyntax pointer:
                Use(pointer.ElementType, kind);
                break;
            case RefTypeSyntax reference:
                Use(reference.Type, kind);
                break;
            case ScopedTypeSyntax scoped:
                Use(scoped.Type, kind);
                break;
            case TupleTypeSyntax tuple:
                foreach (var element in tuple.Elements)
                {
                    Use(element.Type, kind);
                }

                break;
            case FunctionPointerTypeSyntax functionPointer:
                foreach (var parameter in functionPointer.ParameterList.Parameters)
                {
                    Use(parameter.Type, kind);
                }

                break;
            case OmittedTypeArgumentSyntax:
                break;
            case IdentifierNameSyntax name when model.GetAliasInfo(name, scope.CancellationToken) is not null:
                Aliased(model.GetTypeInfo(syntax, scope.CancellationToken).Type, kind, syntax);
                break;
            default:
                Record(model.GetTypeInfo(syntax, scope.CancellationToken).Type, kind, syntax);
                break;
        }
    }

    // The same sugar as Use, on the symbol: an alias hides the named type's syntax, type arguments included.
    private void Aliased(ITypeSymbol? type, DependencyKind kind, SyntaxNode at)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                Aliased(array.ElementType, kind, at);
                break;
            case IPointerTypeSymbol pointer:
                Aliased(pointer.PointedAtType, kind, at);
                break;
            case INamedTypeSymbol { IsTupleType: true } tuple:
                foreach (var element in tuple.TupleElements)
                {
                    Aliased(element.Type, kind, at);
                }

                break;
            case INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable:
                Aliased(nullable.TypeArguments[0], kind, at);
                break;
            case INamedTypeSymbol named:
                Record(named, kind, at);
                for (var current = named; current is not null; current = current.ContainingType)
                {
                    foreach (var argument in current.TypeArguments)
                    {
                        Aliased(argument, DependencyKind.GenericArg, at);
                    }
                }

                break;
        }
    }

    private void Record(ITypeSymbol? type, DependencyKind kind, SyntaxNode at)
    {
        // Type parameters, dynamic, arrays and pointers are no types to depend on; an unresolved type is no Dependency.
        if (source is not { } from || type is not INamedTypeSymbol { TypeKind: not TypeKind.Error } named)
        {
            return;
        }

        var target = named.OriginalDefinition;
        if (target.SpecialType == SpecialType.System_Void || SymbolEqualityComparer.Default.Equals(target, from.Type))
        {
            return;
        }

        facts.Add(new DependencyFact(
            from.Key,
            Symbols.TypeKey(target, scope.Context),
            kind,
            document,
            Source.Lines(at.SyntaxTree, at.Span, scope.CancellationToken).Start,
            scope.Generated.CodeIsGenerated(from.Type, at, member)));
    }
}
