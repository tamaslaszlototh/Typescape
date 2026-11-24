using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>
/// The Dependencies one document's syntax shows, one fact per place: from the innermost own type around it to the
/// named type's open definition. A type's use of itself is no Dependency. A place in a lambda or local function belongs
/// to the Executable Member around it.
/// </summary>
internal sealed class Dependencies : CSharpSyntaxWalker
{
    private readonly SemanticModel model;
    private readonly string document;
    private readonly ProjectScope scope;
    private readonly List<DependencyFact> facts = [];
    private (INamedTypeSymbol Type, string Key)? source;
    private ISymbol? member;
    private string? executable;
    private bool inNameof;

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
        Parameters(node.ParameterList, DependencyKind.Signature);
        base.VisitExtensionBlockDeclaration(node);
    }

    public override void VisitGlobalStatement(GlobalStatementSyntax node)
    {
        var unit = (CompilationUnitSyntax)node.Parent!;
        if (model.GetDeclaredSymbol(unit, scope.CancellationToken) is { ContainingType: { } program })
        {
            InSource(program, () => InMember(unit, () => base.VisitGlobalStatement(node)));
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
            Parameters(node.ParameterList, DependencyKind.Signature);
            base.VisitIndexerDeclaration(node);
        });

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Use(node.ReturnType, DependencyKind.Signature);
            Parameters(node.ParameterList, DependencyKind.Signature);
            base.VisitMethodDeclaration(node);
        });

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Parameters(node.ParameterList, DependencyKind.Signature);
            base.VisitConstructorDeclaration(node);
        });

    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node) =>
        InMember(node, () => base.VisitDestructorDeclaration(node));

    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Use(node.ReturnType, DependencyKind.Signature);
            Parameters(node.ParameterList, DependencyKind.Signature);
            base.VisitOperatorDeclaration(node);
        });

    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node) =>
        InMember(node, () =>
        {
            Use(node.Type, DependencyKind.Signature);
            Parameters(node.ParameterList, DependencyKind.Signature);
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

    // An expression-bodied property or indexer is its getter.
    public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
    {
        if (node.Parent is BasePropertyDeclarationSyntax && member is IPropertySymbol { GetMethod: { } getter })
        {
            InMember(getter, node, () => base.VisitArrowExpressionClause(node));
        }
        else
        {
            base.VisitArrowExpressionClause(node);
        }
    }

    // A constraint is part of the generic declaration's signature. A local function's belongs to its body.
    public override void VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax node)
    {
        var kind = node.Parent is LocalFunctionStatementSyntax ? DependencyKind.TypeUsage : DependencyKind.Signature;
        foreach (var constraint in node.Constraints.OfType<TypeConstraintSyntax>())
        {
            Use(constraint.Type, kind);
        }

        base.VisitTypeParameterConstraintClause(node);
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        Use(node.ReturnType, DependencyKind.TypeUsage);
        Parameters(node.ParameterList, DependencyKind.TypeUsage);
        base.VisitLocalFunctionStatement(node);
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        if (node.ReturnType is { } returnType)
        {
            Use(returnType, DependencyKind.TypeUsage);
        }

        Parameters(node.ParameterList, DependencyKind.TypeUsage);
        base.VisitParenthesizedLambdaExpression(node);
    }

    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
        Parameters(node.ParameterList, DependencyKind.TypeUsage);
        base.VisitAnonymousMethodExpression(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        Use(node.Type, DependencyKind.New);
        base.VisitObjectCreationExpression(node);
    }

    // The created type is the target type, which the code does not write: its type arguments are no generic-arg.
    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        var created = model.GetTypeInfo(node, scope.CancellationToken).Type;
        Record(created is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : created, DependencyKind.New, node);
        base.VisitImplicitObjectCreationExpression(node);
    }

    // An array holds its element type but creates none of it.
    public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitArrayCreationExpression(node);
    }

    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitStackAllocArrayCreationExpression(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var info = model.GetSymbolInfo(node, scope.CancellationToken);
        var called = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        if (called is null && node.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } && node.ArgumentList.Arguments is [var argument])
        {
            Nameof(node, argument.Expression);
            return;
        }

        if (called is IMethodSymbol method)
        {
            Record(Owner(method), DependencyKind.Call, InvokedName(node.Expression));
        }

        base.VisitInvocationExpression(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        Accessed(node);
        base.VisitIdentifierName(node);
    }

    public override void VisitGenericName(GenericNameSyntax node)
    {
        Accessed(node);
        base.VisitGenericName(node);
    }

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        Indexed(node);
        base.VisitElementAccessExpression(node);
    }

    public override void VisitElementBindingExpression(ElementBindingExpressionSyntax node)
    {
        Indexed(node);
        base.VisitElementBindingExpression(node);
    }

    public override void VisitImplicitElementAccess(ImplicitElementAccessSyntax node)
    {
        Indexed(node);
        base.VisitImplicitElementAccess(node);
    }

    // A local's, or a using, for or fixed statement's; a field's or event's is member-type.
    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        if (node.Parent is not BaseFieldDeclarationSyntax)
        {
            Use(node.Type, DependencyKind.TypeUsage);
        }

        base.VisitVariableDeclaration(node);
    }

    public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitDeclarationExpression(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitForEachStatement(node);
    }

    public override void VisitCatchDeclaration(CatchDeclarationSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitCatchDeclaration(node);
    }

    public override void VisitFromClause(FromClauseSyntax node)
    {
        if (node.Type is { } type)
        {
            Use(type, DependencyKind.TypeUsage);
        }

        base.VisitFromClause(node);
    }

    public override void VisitJoinClause(JoinClauseSyntax node)
    {
        if (node.Type is { } type)
        {
            Use(type, DependencyKind.TypeUsage);
        }

        base.VisitJoinClause(node);
    }

    public override void VisitCastExpression(CastExpressionSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitCastExpression(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        switch (node.Kind())
        {
            // `x is Color.Red` is a constant pattern that parses like a type.
            case SyntaxKind.IsExpression:
                PatternType(node.Right);
                break;
            case SyntaxKind.AsExpression when node.Right is TypeSyntax type:
                Use(type, DependencyKind.TypeUsage);
                break;
        }

        base.VisitBinaryExpression(node);
    }

    public override void VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitTypeOfExpression(node);
    }

    public override void VisitSizeOfExpression(SizeOfExpressionSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitSizeOfExpression(node);
    }

    public override void VisitDefaultExpression(DefaultExpressionSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitDefaultExpression(node);
    }

    public override void VisitDeclarationPattern(DeclarationPatternSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitDeclarationPattern(node);
    }

    public override void VisitTypePattern(TypePatternSyntax node)
    {
        Use(node.Type, DependencyKind.TypeUsage);
        base.VisitTypePattern(node);
    }

    public override void VisitRecursivePattern(RecursivePatternSyntax node)
    {
        if (node.Type is { } type)
        {
            Use(type, DependencyKind.TypeUsage);
        }

        base.VisitRecursivePattern(node);
    }

    // The parser cannot tell a type from a constant here, so `case Foo:` and `is Foo or Bar` name types this way.
    public override void VisitConstantPattern(ConstantPatternSyntax node)
    {
        PatternType(node.Expression);
        base.VisitConstantPattern(node);
    }

    public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        PatternType(node.Value);
        base.VisitCaseSwitchLabel(node);
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

                    Parameters(declaration.ParameterList, DependencyKind.Signature);
                    PositionalProperties(type, declaration.ParameterList);
                    break;
                case DelegateDeclarationSyntax @delegate:
                    Use(@delegate.ReturnType, DependencyKind.Signature);
                    Parameters(@delegate.ParameterList, DependencyKind.Signature);
                    break;
            }

            visitChildren();
        });
    }

    private void InSource(INamedTypeSymbol type, Action visit)
    {
        var outer = (source, member, executable);
        (source, member, executable) = ((type, Symbols.TypeKey(type, scope.Context)), null, null);
        visit();
        (source, member, executable) = outer;
    }

    private void InMember(SyntaxNode declaration, Action visit)
    {
        if (model.GetDeclaredSymbol(declaration, scope.CancellationToken) is { } declared)
        {
            InMember(declared, declaration, visit);
        }
        else
        {
            visit();
        }
    }

    private void InMember(ISymbol declared, SyntaxNode declaration, Action visit)
    {
        var outer = (member, executable);
        member = declared;
        executable = declared is IMethodSymbol method && source is { } from ? ExecutableMembers.Key(method, declaration, from.Key) : null;
        visit();
        (member, executable) = outer;
    }

    // The type nameof names, or the type of the member it names; nothing in its argument is accessed or called.
    private void Nameof(InvocationExpressionSyntax node, ExpressionSyntax argument)
    {
        var info = model.GetSymbolInfo(argument, scope.CancellationToken);
        var named = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        switch (named)
        {
            case ITypeSymbol type:
                Record(type, DependencyKind.TypeUsage, argument);
                break;
            case IFieldSymbol or IPropertySymbol or IEventSymbol or IMethodSymbol:
                Record(Owner(named), DependencyKind.TypeUsage, argument);
                break;
        }

        var outer = inNameof;
        inNameof = true;
        base.VisitInvocationExpression(node);
        inNameof = outer;
    }

    // A field, property or event the code reads or writes, or a method group it does not call.
    private void Accessed(SimpleNameSyntax name)
    {
        if (inNameof || source is null)
        {
            return;
        }

        var accessed = model.GetSymbolInfo(name, scope.CancellationToken).Symbol;
        switch (accessed)
        {
            // A tuple element is sugar for no ValueTuple field.
            case IFieldSymbol { CorrespondingTupleField: not null }:
                break;
            case IFieldSymbol or IPropertySymbol or IEventSymbol:
                Record(Owner(accessed), DependencyKind.MemberAccess, name);
                break;
            // An attribute's name binds to its constructor.
            case IMethodSymbol { MethodKind: not MethodKind.Constructor } method when !IsInvoked(name):
                Record(Owner(method), DependencyKind.MemberAccess, name);
                break;
        }
    }

    private void Indexed(ExpressionSyntax access)
    {
        if (!inNameof && model.GetSymbolInfo(access, scope.CancellationToken).Symbol is IPropertySymbol indexer)
        {
            Record(Owner(indexer), DependencyKind.MemberAccess, access);
        }
    }

    private void PatternType(ExpressionSyntax expression)
    {
        if (expression is TypeSyntax type && model.GetSymbolInfo(type, scope.CancellationToken).Symbol is ITypeSymbol)
        {
            Use(type, DependencyKind.TypeUsage);
        }
    }

    private static bool IsInvoked(SimpleNameSyntax name)
    {
        SyntaxNode expression = name.Parent is MemberAccessExpressionSyntax access && access.Name == name || name.Parent is MemberBindingExpressionSyntax
            ? name.Parent
            : name;
        return expression.Parent is InvocationExpressionSyntax invocation && invocation.Expression == expression;
    }

    // The line of the method's name, not of the receiver a call chain starts with.
    private static SyntaxNode InvokedName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name,
        MemberBindingExpressionSyntax binding => binding.Name,
        _ => expression,
    };

    // An extension block is no type: its members belong to the static class around it.
    private static INamedTypeSymbol? Owner(ISymbol member) =>
        member.ContainingType is { IsExtension: true } block ? block.ContainingType : member.ContainingType;

    private DependencyKind BaseKind(INamedTypeSymbol type, TypeSyntax baseType) =>
        type.TypeKind != TypeKind.Interface && model.GetTypeInfo(baseType, scope.CancellationToken).Type is { TypeKind: TypeKind.Interface }
            ? DependencyKind.Implements
            : DependencyKind.Inherits;

    private void Parameters(BaseParameterListSyntax? parameters, DependencyKind kind)
    {
        foreach (var parameter in parameters?.Parameters ?? [])
        {
            if (parameter.Type is { } type)
            {
                Use(type, kind);
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
            // var writes no type, unless a type is named var.
            case IdentifierNameSyntax { IsVar: true } name when model.GetSymbolInfo(name, scope.CancellationToken).Symbol is not INamedTypeSymbol { Name: "var" }:
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
        // An anonymous type has no key.
        if (source is not { } from || type is not INamedTypeSymbol { TypeKind: not TypeKind.Error, IsAnonymousType: false } named)
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
            executable,
            Symbols.TypeKey(target, scope.Context),
            kind,
            document,
            Source.Lines(at.SyntaxTree, at.Span, scope.CancellationToken).Start,
            scope.Generated.CodeIsGenerated(from.Type, at, member)));
    }
}
