using LibGit2Sharp;

namespace GitRocketFilter
{
    public abstract class RocketScriptBase
    {
        private readonly RocketFilterApp rocketFilterApp;

        protected RocketScriptBase(RocketFilterApp rocketFilterApp)
        {
            this.rocketFilterApp = rocketFilterApp;
        }

        public SimpleCommit Map(SimpleCommit commit)
        {
            return rocketFilterApp.GetMapCommit(commit);
        }

        public SimpleCommit Simple(Commit commit)
        {
            return rocketFilterApp.GetSimpleCommit(commit);
        }
    }
}