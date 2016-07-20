using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace InitializeFieldFromConstructor
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(InitializeFieldFromConstructorCodeRefactoringProvider)), Shared]
    internal class InitializeFieldFromConstructorCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            SyntaxNode root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // find the node at the selection.
            SyntaxNode node = root.FindNode(context.Span);

            // only offer a refactoring if the selected node is a field declaration.
            VariableDeclaratorSyntax variableDeclarator = node as VariableDeclaratorSyntax;
            FieldDeclarationSyntax fieldDeclaration = variableDeclarator?.Ancestors().OfType<FieldDeclarationSyntax>().SingleOrDefault();
            if (fieldDeclaration == null)
            {
                return;
            }

            // create the code action
            CodeAction action = CodeAction.Create("Initialize field from constructor",
                c => InitializeFromConstructor(context.Document, fieldDeclaration, variableDeclarator, c));

            // register this code action.
            context.RegisterRefactoring(action);
        }

        private async Task<Document> InitializeFromConstructor(Document document, FieldDeclarationSyntax fieldDeclaration, VariableDeclaratorSyntax fieldVariable, CancellationToken cancellationToken)
        {
            ClassDeclarationSyntax classDeclaration = fieldDeclaration.Ancestors().OfType<ClassDeclarationSyntax>().Single();

            string parameterType = fieldDeclaration.Declaration.Type.ToString();
            SyntaxToken identifierToken = fieldVariable.Identifier;
            string parameterName = identifierToken.Text.TrimStart('_');

            // create the new parameter
            ParameterSyntax parameter = Parameter(
                Identifier(parameterName))
                .WithType(IdentifierName(parameterType)
            );

            // create the new assignment to add to the constructor body
            ExpressionStatementSyntax assignment = ExpressionStatement(
                AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, 
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(identifierToken)), 
                    IdentifierName(parameterName)));

            // get the syntax root
            SyntaxNode root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            // get existing constructor if any
            ConstructorDeclarationSyntax existingConstructor = classDeclaration.DescendantNodes().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();

            // update the class by either updating the constructor, or adding one
            SyntaxNode updatedClassDecl;
            if (existingConstructor != null)
            {
                ConstructorDeclarationSyntax updatedConstructor = existingConstructor
                    .AddParameterListParameters(parameter)
                    .AddBodyStatements(assignment);

                updatedClassDecl = classDeclaration.ReplaceNode(existingConstructor, updatedConstructor);
            }
            else
            {
                ConstructorDeclarationSyntax constructor = ConstructorDeclaration(classDeclaration.Identifier.Text)
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .WithParameterList(ParameterList(
                        SingletonSeparatedList(parameter)
                    ))
                    .WithBody(Block(assignment))
                    .WithLeadingTrivia(fieldVariable.GetLeadingTrivia().Insert(0, CarriageReturnLineFeed));

                updatedClassDecl = classDeclaration.InsertNodesAfter(fieldDeclaration, new [] { constructor });
            }

            // replace the root node with the updated class
            SyntaxNode newRoot = root.ReplaceNode(classDeclaration, updatedClassDecl);

            return document.WithSyntaxRoot(newRoot);
        }

        /*
            // Get the symbol representing the type to be renamed.
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);

            var originalSolution = document.Project.Solution;
            var optionSet = originalSolution.Workspace.Options;
            //var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, typeSymbol, newName, optionSet, cancellationToken).ConfigureAwait(false);
         */

    }
}