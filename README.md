Hello and thank you! We know interviewing is time consuming and stressful, and we appreciate
you taking the time to see this process through with Tonic. This interview is important to us
because it simulates several aspects of working at Tonic:

1. The problem you're about to work on is _very_ similar to the kinds of problems you may
work on at Tonic. If you like this exercise, you'll probably enjoy working on the challenges
we face at Tonic.
2. Tonic is largely a remote company. We do get together at our hub offices, but most of your work
will be independent, just like this.
3. Most people don't work with an interviewer watching their every move. We feel this setting gives
folks a chance to work more at their own pace.

# Some Advice

Before you get started here are some things to keep in mind:

1. Use whatever resources you want. If you were on the job, Stack Overflow and ChatGPT would
be fair game, and they are fair game here too. Use them as much as you would normally. With that said, all your
work should be your own, no phoning in a friend.
2. Don't go too deep. This problem can be optimized and optimized and optimized. We ask
that you spend no more than 3 hours on this exercise. This isn't a timed exercise, you
don't need a stop watch, but for your sake, don't over do it.
3. We are going to use the same database and config files we're providing you to test your submission, and
there aren't any additional configurations or databases we will test your code against. **There are no hidden
test cases we're going to spring on your code to test things outside the examples we're providing.** In other words,
don't spend time fixing issues that would occur outside the database and configurations we provide.
For example, your code doesn't need to handle `hstore` columns, foreign keys, or other database extensions (e.g. PostGIS). With that said,
don't hardcode your solution to the particular configurations we provide; the code should be generic.
4. There are many things that can problably be improved about the code we've provided. We're only asking that
you address the performance questions we talked about in the previous interview.
5. Quality over quantity. It's better to have one idea that's fully realized than a smattering of half baked ones.
6. Make sure your program works before submitting it.

# Instructions

Open the starter code for your language of choice, C#, Java or C++. Pick whichever language
you feel strongest in, we aren't judging your choice. For IDE we tested our projects with:

* Visual Studio Code and JetBrains Rider for C#,
* JetBrains IntelliJ Community Edition for Java,
* JetBrains CLion for C++.

Once your IDE is setup with the code, try running the project. Run the project with
no arguments; you should receive an error indicating that you must provide a configuration. For
example, here is the output for the C# program with no arguments,

```
Need to supply a TOML file configuration.
dotnet data_mover.dll <configuration.toml>
```

Before we run it with data, you'll need to load a test database. For this exercise we are providing a
Postgres database dump.

## Database Dump

The database dump is `interview.pgdump.gz`.
The database contains 8 tables in the `public` schema of a Postgres database. You may
use this database dump with Postgres 14 or higher. To restore this dump create a
Postgres database named `source`, unzip the `interview.pgdump.gz` file, and run the following command:

```
psql <connection options> -d source -f interview.pgdump
```

In case you're curious, the origin of the data is a sample of rows from the `mrk_label` table of the
[Mouse Genome Database](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC165494/).

## Running data_mover

You need to do a few more things before getting started.

1. Create a second database named `destination` in your Postgres instance.
2. Open `configs/small.toml` and `configs/full.toml` and update the connection
details to match the database you've setup.

Start by running the program with `configs/small.toml`. The output should look like this,

```
Starting table "public"."small1".
Table TableConfiguration { Table = "public"."small1", Limit =  } processed in: 3.4580783s
Starting table "public"."small2".
Table TableConfiguration { Table = "public"."small2", Limit =  } processed in: 2.9048914s
Starting table "public"."small3".
Table TableConfiguration { Table = "public"."small3", Limit =  } processed in: 3.0553812s
Starting table "public"."small4".
Table TableConfiguration { Table = "public"."small4", Limit =  } processed in: 3.0792606s
Total time processing tables: 12.5822604s
```

That's the C# output. Other languages will look similar.

Alright. That's it. Take another look at the section named "Some Advice", and have some fun with it!
