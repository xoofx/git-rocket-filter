# git-rocket-filter [![NuGet](https://img.shields.io/nuget/v/git-rocket-filter.svg)](https://www.nuget.org/packages/git-rocket-filter/)

<img align="right" width="160px" height="160px" src="img/rocket.png">

Powerful and fast command line tool to rewrite git branches powered by .NET, [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) and [Roslyn](https://github.com/dotnet/roslyn).

## Synopsis


    git-rocket-filter [-b|--branch  <branch_name>]  [--force]
                      [-c|--commit-filter <command>]  [--commit-filter-script <script>]  
                      [--detach]  
                      [-k|--keep <patternAndCommand>]  [--keep-script <script>]
                      [-r|--remove <patternAndCommand>]  [--remove-script <script>]
                      [--include-links]     
                      [-d|--repo-dir <repo_path>]  [-h|--help]  [-v|--verbose]
                      [<revision>]

## Description

The purpose of `git-rocket-filter` is similar to the command [`git-filter-branch`](http://git-scm.com/docs/git-filter-branch) while providing the following unique features:

- Fast rewriting of commits and trees (by an order of `x10` to `x100`).
- Built-in support for both **white-listing** with `--keep` (keeps files or directories) and **black-listing** with `--remove` options.
- Use of `.gitignore` like pattern for tree-filtering 
- Fast and easy C# Scripting for both commit filtering and tree filtering
- Support for scripting in tree-filtering per file/directory pattern
- Automatically prune empty/unchanged commit, including merge commits

Tested on Windows and Linux (with Mono installed).


> ** Warning **
> Usage of this command has the same warning than git-filter-branch: *"The rewritten history will have different object names for all the objects and will not converge with the original branch. You will not be able to easily push and distribute the rewritten branch on top of the original branch. Please do not use this command if you do not know the full implications, and avoid using it anyway, if a simple single commit would suffice to fix your problem."* 
> (from [git-filter-branch](http://git-scm.com/docs/git-filter-branch) documentation)

## Download

You can install `git-rocket-filter` as a .NET global tool with the following command:

``` 
dotnet tool install -g git-rocket-filter
```

Alternatively, you can also install the "dockerized" version:

```
curl https://raw.githubusercontent.com/jcfr/dockgit-rocket-filter/master/git-rocket-filter.sh \
  -o ~/bin/git-rocket-filter && \
chmod +x ~/bin/git-rocket-filter
```

Notes:
* the containerized version may not be the latest one.
* this approach is suitable on platform with `docker` and a `unix shell` installed (Linux, macOS, Windows Subsystem for Linux)

## Examples

**Change commit messages:**

    git-rocket-filter --branch TestBranch 
                      --commit-filter 'commit.Message += "Added by git-rocket-filter";'

Rewrite all commits by adding the message "Added by git-rocket-filter" and store the results in the new branch `TestBranch`

**Change commit author name and email:**

    git-rocket-filter --branch TestBranch --commit-filter '
        if (commit.AuthorName.Contains("Jim")) {   
            commit.AuthorName = "Paul"; 
            commit.AuthorEmail = "paul@company.com"; 
        }
    '

Rewrite commits authored by Jim and change the author name and email to Paul, storing the results in the new branch `TestBranch`

**Keep a directory while remove some specific files**:

    git-rocket-filter --branch TestBranch --keep MyDirectory --delete '*.txt'

Keeps only the directory `MyDirectory` except all `*.txt` files and store the results of the rewrite to the new branch `TestBranch` 

**Removes files bigger than 1Mo**:

    git-rocket-filter --branch TestBranch 
                      --delete '* => entry.Discard = entry.Size > 1024*1024;''

Removes all files bigger than 1Mo and store results to the new branch `TestBranch` 

> **Note For Windows Users**
> The command lines above are valid for a bash shell on an Unix machine. On Windows, depending if you are running a command in a DOS/batch or a msysgit bash, some escape characters may be required in the command line to pass correctly the options (This is especially important if you are passing a C# script code in the command line):
> - In a **DOS/Batch**: you can use the double quote `"` to group pattern/command. For example, you can write the command above `git-rocket-filter --branch TestBranch --delete "* => entry.Discard = entry.Size > 1024*1024;"'. Note that DOS/Batch use [special escape characters](http://www.robvanderwoude.com/escapechars.php) that you should be aware of.
> - In a **Git bash** commonly shipped with msysgit you should be careful about the way [mingw is escaping characters](http://www.mingw.org/wiki/Posix_path_conversion) on Windows. Most notably, if you use a pattern like `--keep /MyDirectory` mingw will expand it to `--keep  C:/Program Files (x86)/Git/bin/MyDirectory` which can lead to unexpected errors!
>
> A safe way to use the command on Windows is to provide command/patterns from script file instead using `--commit-filter-script` or `--keep-script` or `--remove--script`


## Options

#### `-b | --branch <branchName>`


This option is **required**. Unlike git-filter-branch, git-rocket-filter doesn't modify your current branch. Instead, you need to pass a branch name where it will store the results of the filtering.

Note that if the branch already exists, you can force to write to it by using the option `--force`   

___
#### `-c | --commit-filter <command>`

Runs the &lt;command&gt; as a C# script on each commit. The command has access to the following variable:

- `repo`: The `LibGit2Sharp.Repository` object that allows to fully interact with the git repository.
- `commit`: The [`SimpleCommit`](https://github.com/xoofx/GitRocketFilter/blob/master/src/SimpleCommit.cs) object that provides the following properties:


Property               |Access| Description
---------              |------| -----------
commit.Id              |r     | Commit id
commit.Sha             |r     | Commit id as a string
commit.AuthorName      |rw    | Author's name 
commit.AuthorEmail     |rw    | Author's email 
commit.AuthorDate      |rw    | Author's commit date
commit.CommitterName   |rw    | Committer's name 
commit.CommitterEmail  |rw    | Committer's email 
commit.CommitterDate   |rw    | Committer's commit date
commit.Message         |rw    | The commit message
commit.Tree            |rw    | The `LibGit2Sharp.Tree` object
commit.Parents         |r     | A collection of the parent commit `SimpleCommit`
commit.Tag             |rw    | A property to store a user object
commit.Discard         |rw    | A boolean that indicates if we want to keep the commit or discard it. Default is **false**

To **discard a commit**, a command can simply set `if (<condition>) { commit.Discard = true; }` or simply `commit.Discard = <condition>;` to set it based on the result of conditions...etc.

This option is mostly used for **commit-filtering** (that can be used together with tree-filtering options like `--keep`, `--remove`...) but as you have access to to the commit.Tree, you can perform also special tree-filtering accessing directly the tree. For simpler cases where you are just looking for keeping/removing files, the simpler options `--keep`/`--remove` are much more suitable and efficient. 
___
#### `--commit-filter-script <script_file>`

Similar to `--commit-filter`, but it reads the script from the given &lt;script_file&gt; 
___
#### `--keep <pattern [=> command | {% multiline command %}]>`  

Keeps the specified file/directory based on a `<pattern and command>` `<pattern> [<command>]` where 
- `<pattern>` is `.gitignore` file pattern 
- `<command>` is an optional script to evaluate on each entry: an optional script to evaluate on each entry.
    - for a one line script use the association `=>` like `=> entry.Discard = entry.Size > 10000;`
    - for a multiline script use the script enclosed by `{%` and `%}` 
    
```
         {% 
            entry.Discard = entry.Size > 10000;
         %}
```

Multiple `--keep` options are accepted.

When passing a script to be evaluated on each matching entry, the following object and properties are accessible:


- `repo`: The `LibGit2Sharp.Repository` object that allows to fully interact with the git repository.
- `commit`: The [`commit`](https://github.com/xoofx/GitRocketFilter/blob/master/src/SimpleCommit.cs) object as described in the commit filter above.
- `entry`: The file [`entry`](https://github.com/xoofx/GitRocketFilter/blob/master/src/SimpleEntry.cs) object that provides the following properties:

Property               |Access| Description
---------              |------| -----------
entry.Id               |r     | Git Object id of the blog (or link if --include-links is specified)
entry.Sha              |r     | Git Object id as a string
entry.Name             |r     | Name of the entry (file name, directory name, git-link name) 
entry.Path             |r     | Full path of the entry (e.g `/my/full/path/entry`)
entry.Size             |r     | If the entry is a blob, size in bytes of the blob
entry.IsBlob           |r     | A boolean indicating if the entry is a git blob
entry.IsBinary         |r     | A boolean indicating if the entry is a binary blob 
entry.IsLink           |r     | A boolean indicating if the entry is a link commit id to a submodule  
entry.Tag             |rw    | A property to store a user object
entry.Discard         |rw    | A boolean that indicates if we want to keep the entry or discard it. Default is **false** for `--keep` matching and **true** for `--remove` matching.

The options `--keep`, `--remove`, `--keep-script`, `--remove-script` are **tree-filtering** operations because you can effectively rewrite the tree (directory/files) associated to a commit.

As the commit object is also accessible in the script, it is still possible to discard a commit from a tree-filtering operation (For example, a tree-filtering could decide to discard a commit based on the content of some files, an author modifying a certain file...etc.).


Example:
- `--keep MyDirectory`: Keeps a directory named `MyDirectory` 
- `--keep '/My/Sub/**/MyFolder'`: Keeps all folders `MyFolder`recursively from the folder `/My/Sub`
- `--keep '* => entry.Discard = entry.Size > 10000;'`: Keeps only files that are less than 10,000 bytes

> Note:
> - For **patterns with scripts**, they are resolved before patterns with no scripts, in the order they are passed to the command line (or in the order of lines from a script file). The first pattern that matches an entry is used for this path without going through remaining patterns. 
> - For **patterns with no scripts**, the rules are squashed in the same way `.gitignore` is squashing them, meaning that order is not relevant in this case. 

___
#### `--keep-script <script_file>`

Similar to `--keep`, but it reads the patterns and commands from a script &lt;script_file&gt;

A typical script file could be:

```
# This is a pattern file
# It is using the same syntax as .gitignore (with additional syntax to attach a script
# per pattern)
# Keeps all files
*
# Except
!file_to_exclude[12].txt
```

Using scripts in the file is also straightfoward (no need to care about escape characters unlike the command line):

**Single Line scripts**

```
# Except files that are stored in this specific location
# Note in this case we put this rule before the wildcard * rules below
# to make sure that this pattern will be called first
/my/files/** => entry.Discard = false;  

# Keep all files that are smaller than 1024x1024 bytes. Note the one-line script using 
# the separator `=>` 

*  => entry.Discard = entry.Size > 1024*1024;
```

**Multi Line scripts**

```
# Multiline script: Keep files that have a specific commit author and file size:

/some/specific/files/**/*.ext    {%

    if (commit.AuthorName.Contains("ThisGuy") && entry.Size > 1024*1024)
    {
        entry.Discard = true;
    }
%} 
```

You can use multiple `--keep` and `--keep-script` options from the same command line.
___
#### `--remove <pattern [=> command | {% multiline command %}]>`  

Removes the specified file/directory based on a `<pattern and command>` `<pattern> [<command>]`. Similar to the way `--keep` is working but by deleting files instead.

The same remarks for `--keep` apply to this option.

Note when using script that the `entry.Discard` is by default set to **true** for each entry visited. You can reverse the behavior by setting **false** on a particular entry.
___
#### `--remove-script <script_file>`

Similar to `--remove`, it reads the patterns and commands from a script &lt;script_file&gt; and contains a .git-ignore list of pattern to remove.

See `--keep-script` for a description about these pattern files.

You can use multiple `--remove` and `--remove-script` options from the same command line.
___
#### `[<revision>]`

By default, git-rocket-filter is working from the first commit to the HEAD on the current branch.

This is equivalent to give the revision: `HEAD` 

Some example of revision format:
- `HEAD` is a reference to the HEAD commit.
- `commitId`: filters until the commitId
- `fromCommitId..toCommitId` filter fromCommitId (non inclusive) to toCommitId (inclusive)
- `HEAD~4..HEAD` filter the last 4 commits accessible from HEAD (going through all merge branches if any)

___
#### `[--detach]`

Use with commit filtering. By default, the commit filtering keeps the original parents of the first commits. Using the `--detach` option allows to completely remove the parents of the first commit. 

This can be useful if you want to extract a tree 
___
#### `[--preserve-merge-commits]` 

By default, git-rocket-filter removes merge commits that don't contain any changes compared to one of the parents. This option ensures that such 'empty' merge commits are preserved.
___
#### `[--include-links]`

By default, in a tree-filtering (`--keep`, `--remove`...), git-rocket-filter doesn't include links to git submodule. You can include links that specifying this option. Note that while accessing the entry in the script you must check whether the entry is a blob with `entry.IsBlob` or a link `entry.IsLink` as some properties are not valid depending on the type (like `entry.Size` only valid for blob).
___
#### `[-d|--repo-dir <repo_path>]`  

By default, git-rocket-filter is expecting to be ran under a git repository. You can specify an alternative directory `<repo_path>` to perform a filtering operations. 
___
#### `[-h|--help]`  

Prints some helps about command in the console.
___
#### `[-v|--verbose]` 

Prints some diagnostic messages about the patterns found and the final generated C# code. 
  
## Implementation details

git-rocket-filter is mostly a combined wrapper around [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) and [Roslyn](https://github.com/dotnet/roslyn).

In order to increase the performance of rewriting commits, git-rocket-filter is built upon:
- .NET Parallel tasks and threads to dispatch the work on multiple cores. The dispatch is done per tree visited and if there is a need to to perform gitignore pattern matching.
- Efficiently caching .gitignore pattern entries from LibGit2Sharp so that we avoid to callback libgit2 to perform pattern matching (which is cpu consuming in libgit2)

### Performance

Extracting the folder `Documentation` from the repository [git](https://github.com/git/git) which is composed of 40,000+ commits takes around 250s (4min).

There are still some areas where `git-rocket-filter` may be inefficient or could be further optimized. For example, `git-rocket-filter` visit the full tree of each commit in order to save the list of entries to keep (entries that can be later selectively removed by a --delete pattern). This visit could be optimized when we know that there won't be any selective patterns, and instead of going deep into the tree, just keep top level trees...

But at least, performance of `git-rocket-filter` should be on average much better than `git-filter-branch`, moreover when a whitelist pattern (--keep) is required.

## License
This software is released under the [BSD-Clause 2 license](http://opensource.org/licenses/BSD-2-Clause). 

## Credits

This tool wouldn't have been developed without using the following components:

- [LibGit2](https://libgit2.github.com/) and [LibGit2Sharp](https://github.com/libgit2/libgit2sharp):  libgit2 is a portable, pure C implementation of the Git core methods provided as a re-entrant linkable library with a solid API, allowing you to write native speed custom Git applications in any language which supports C bindings. LigGit2Sharp are the bindings of libgit2 for .NET. 

> Note that git-rocket-filter is still using a [forked version of LibGit2Sharp](https://github.com/xoofx/libgit2sharp/tree/git-rocket-filter) while [waiting the PR](https://github.com/libgit2/libgit2sharp/pulls/xoofx) to be accepted by the LibGit2Sharp project. 

- [Roslyn](https://github.com/dotnet/roslyn):  The .NET Compiler Platform ("Roslyn") provides open-source C# and Visual Basic compilers with rich code analysis APIs.

# Author

Alexandre Mutel aka [xoofx](http://xoofx.com).
