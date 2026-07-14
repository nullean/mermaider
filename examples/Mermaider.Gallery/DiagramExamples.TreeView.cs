namespace Mermaider.Gallery;

public static partial class DiagramExamples
{
	private static DiagramExample[] CreateTreeViewExamples() =>
	[
		new("treeview-basic", "Basic Project Structure", DiagramCategory.TreeView, """
			treeView-beta
			    my-project/
			        src/
			            index.js
			            utils.js
			        tests/
			            index.test.js
			        package.json
			        README.md
			"""),

		new("treeview-dotnet", ".NET Solution", DiagramCategory.TreeView, """
			treeView-beta
			    MyApp.sln/
			        src/
			            MyApp/
			                Program.cs
			                appsettings.json
			                MyApp.csproj
			            MyApp.Core/
			                Services/
			                    UserService.cs
			                    AuthService.cs
			                Models/
			                    User.cs
			                MyApp.Core.csproj
			        tests/
			            MyApp.Tests/
			                UserServiceTests.cs
			                MyApp.Tests.csproj
			        README.md
			"""),

		new("treeview-box-drawing", "Box-Drawing Format", DiagramCategory.TreeView, """
			treeView-beta
			├── src/
			│   ├── components/
			│   │   ├── Header.tsx
			│   │   ├── Footer.tsx
			│   │   └── Sidebar.tsx
			│   ├── pages/
			│   │   ├── Home.tsx
			│   │   └── About.tsx
			│   └── App.tsx
			├── public/
			│   └── index.html
			├── package.json
			└── tsconfig.json
			"""),

		new("treeview-highlighted", "Highlighted Nodes", DiagramCategory.TreeView, """
			treeView-beta
			    project/
			        src/
			            main.ts :::highlight ## entry point
			            config.ts
			        dist/
			            main.js :::highlight ## build output
			        package.json
			"""),

		new("treeview-descriptions", "With Descriptions", DiagramCategory.TreeView, """
			treeView-beta
			    infrastructure/
			        terraform/
			            main.tf ## root module
			            variables.tf ## input variables
			            outputs.tf ## exported values
			        docker/
			            Dockerfile ## multi-stage build
			            docker-compose.yml ## local dev stack
			        k8s/
			            deployment.yaml ## pod spec
			            service.yaml ## load balancer
			            ingress.yaml ## external routing
			"""),

		new("treeview-icons", "Custom Icons", DiagramCategory.TreeView, """
			treeView-beta
			    repo/ icon(folder-open)
			        src/ icon(folder-open)
			            app.ts icon(file:code)
			            styles.css icon(file:code)
			            logo.png icon(file:image)
			        docs/
			            guide.md icon(file:document)
			            data.csv icon(file:data)
			        .env icon(file:config)
			        package.json icon(file:config)
			"""),

		new("treeview-monorepo", "Monorepo Layout", DiagramCategory.TreeView, """
			treeView-beta
			    monorepo/
			        packages/
			            core/
			                src/
			                    index.ts
			                package.json
			            ui/
			                src/
			                    Button.tsx
			                    Modal.tsx
			                package.json
			            cli/
			                src/
			                    main.ts
			                package.json
			        apps/
			            web/
			                src/
			                    App.tsx
			                package.json
			            api/
			                src/
			                    server.ts
			                package.json
			        turbo.json
			        package.json
			"""),

		new("treeview-mixed-annotations", "Combined Annotations", DiagramCategory.TreeView, """
			treeView-beta
			    deploy/ icon(folder-open)
			        Dockerfile :::highlight ## production image
			        nginx.conf ## reverse proxy config
			        certs/
			            server.crt icon(file:config)
			            server.key icon(file:config) :::highlight
			        scripts/
			            deploy.sh ## CI/CD entry point
			            rollback.sh
			"""),

		new("treeview-quoted-labels", "Quoted Labels with Spaces", DiagramCategory.TreeView, """
			treeView-beta
			    "my project"/
			        "source files"/
			            "main module.ts"
			            "helper utils.ts"
			        "config files"/
			            "app config.json"
			            "env vars.env"
			        "README (important).md" :::highlight
			"""),

		new("treeview-heavy-box", "Heavy Box-Drawing", DiagramCategory.TreeView, """
			treeView-beta
			┣━━ src/
			┃   ┣━━ lib/
			┃   ┃   ┣━━ parser.rs
			┃   ┃   ┗━━ lexer.rs
			┃   ┗━━ main.rs
			┣━━ Cargo.toml
			┗━━ README.md
			"""),

		new("treeview-deep-nesting", "Deep Nesting", DiagramCategory.TreeView, """
			treeView-beta
			    app/
			        domain/
			            user/
			                commands/
			                    CreateUser.cs
			                    UpdateUser.cs
			                queries/
			                    GetUserById.cs
			                    ListUsers.cs
			                events/
			                    UserCreated.cs
			                    UserUpdated.cs
			                User.cs
			            order/
			                commands/
			                    PlaceOrder.cs
			                Order.cs
			"""),
	];
}
