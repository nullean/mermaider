using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class TreeViewParserTests
{
	[Test]
	public void Parses_basic_indentation_tree()
	{
		var lines = new[]
		{
			"treeView-beta",
			"    project/",
			"        src/",
			"            index.js",
			"        README.md",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots.Should().HaveCount(1);
		diagram.Roots[0].Label.Should().Be("project");
		diagram.Roots[0].IsDirectory.Should().BeTrue();
		diagram.Roots[0].Children.Should().HaveCount(2);
		diagram.Roots[0].Children[0].Label.Should().Be("src");
		diagram.Roots[0].Children[0].IsDirectory.Should().BeTrue();
		diagram.Roots[0].Children[0].Children.Should().HaveCount(1);
		diagram.Roots[0].Children[0].Children[0].Label.Should().Be("index.js");
		diagram.Roots[0].Children[0].Children[0].IsDirectory.Should().BeFalse();
		diagram.Roots[0].Children[1].Label.Should().Be("README.md");
	}

	[Test]
	public void Parses_box_drawing_standard()
	{
		var lines = new[]
		{
			"treeView-beta",
			"├── src/",
			"│   ├── main.ts",
			"│   └── utils.ts",
			"└── package.json",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots.Should().HaveCount(2);
		diagram.Roots[0].Label.Should().Be("src");
		diagram.Roots[0].IsDirectory.Should().BeTrue();
		diagram.Roots[0].Children.Should().HaveCount(2);
		diagram.Roots[0].Children[0].Label.Should().Be("main.ts");
		diagram.Roots[0].Children[1].Label.Should().Be("utils.ts");
		diagram.Roots[1].Label.Should().Be("package.json");
	}

	[Test]
	public void Parses_box_drawing_heavy()
	{
		var lines = new[]
		{
			"treeView-beta",
			"┣━━ src/",
			"┃   ┗━━ main.rs",
			"┗━━ Cargo.toml",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots.Should().HaveCount(2);
		diagram.Roots[0].Label.Should().Be("src");
		diagram.Roots[0].IsDirectory.Should().BeTrue();
		diagram.Roots[0].Children.Should().HaveCount(1);
		diagram.Roots[0].Children[0].Label.Should().Be("main.rs");
		diagram.Roots[1].Label.Should().Be("Cargo.toml");
	}

	[Test]
	public void Detects_directories_by_trailing_slash()
	{
		var lines = new[]
		{
			"treeView-beta",
			"    src/",
			"    file.txt",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots[0].IsDirectory.Should().BeTrue();
		diagram.Roots[0].Label.Should().Be("src");
		diagram.Roots[1].IsDirectory.Should().BeFalse();
		diagram.Roots[1].Label.Should().Be("file.txt");
	}

	[Test]
	public void Parses_quoted_labels()
	{
		var lines = new[]
		{
			"treeView-beta",
			"    \"my project\"/",
			"        \"file with spaces.txt\"",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots[0].Label.Should().Be("my project");
		diagram.Roots[0].IsDirectory.Should().BeTrue();
		diagram.Roots[0].Children[0].Label.Should().Be("file with spaces.txt");
	}

	[Test]
	public void Parses_class_annotation()
	{
		var lines = new[]
		{
			"treeView-beta",
			"    important.ts :::highlight",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots[0].CssClass.Should().Be("highlight");
		diagram.Roots[0].Label.Should().Be("important.ts");
	}

	[Test]
	public void Parses_description_annotation()
	{
		var lines = new[]
		{
			"treeView-beta",
			"    main.ts ## entry point",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots[0].Description.Should().Be("entry point");
		diagram.Roots[0].Label.Should().Be("main.ts");
	}

	[Test]
	public void Parses_icon_annotation()
	{
		var lines = new[]
		{
			"treeView-beta",
			"    app.ts icon(file:code)",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots[0].Icon.Should().Be("file:code");
		diagram.Roots[0].Label.Should().Be("app.ts");
	}

	[Test]
	public void Parses_icon_none_suppresses()
	{
		var lines = new[]
		{
			"treeView-beta",
			"    hidden.txt icon(none)",
			"    empty.txt icon()",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots[0].Icon.Should().Be("");
		diagram.Roots[1].Icon.Should().Be("");
	}

	[Test]
	public void Parses_combined_annotations()
	{
		var lines = new[]
		{
			"treeView-beta",
			"    main.ts icon(file:code) :::highlight ## entry point",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots[0].Label.Should().Be("main.ts");
		diagram.Roots[0].Icon.Should().Be("file:code");
		diagram.Roots[0].CssClass.Should().Be("highlight");
		diagram.Roots[0].Description.Should().Be("entry point");
	}

	[Test]
	public void Returns_empty_for_minimal_input()
	{
		var lines = new[] { "treeView-beta" };

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots.Should().BeEmpty();
	}

	[Test]
	public void Skips_empty_lines()
	{
		var lines = new[]
		{
			"treeView-beta",
			"",
			"    src/",
			"",
			"        main.ts",
			"",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots.Should().HaveCount(1);
		diagram.Roots[0].Children.Should().HaveCount(1);
	}

	[Test]
	public void Expands_tabs_to_spaces()
	{
		var lines = new[]
		{
			"treeView-beta",
			"\tproject/",
			"\t\tsrc/",
			"\t\t\tindex.js",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots.Should().HaveCount(1);
		diagram.Roots[0].Label.Should().Be("project");
		diagram.Roots[0].Children[0].Label.Should().Be("src");
		diagram.Roots[0].Children[0].Children[0].Label.Should().Be("index.js");
	}

	[Test]
	public void Multiple_roots_in_indentation_mode()
	{
		var lines = new[]
		{
			"treeView-beta",
			"    file1.txt",
			"    file2.txt",
			"    dir/",
			"        nested.txt",
		};

		var diagram = TreeViewParser.Parse(lines);

		diagram.Roots.Should().HaveCount(3);
		diagram.Roots[2].IsDirectory.Should().BeTrue();
		diagram.Roots[2].Children.Should().HaveCount(1);
	}
}
