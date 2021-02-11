namespace ReleaseNotesMaker.Model
{
    public class GitHubTextFile
    {
        public string Content
        {
            get;
            set;
        }

        public string Path
        {
            get;
            set;
        }

        public override string ToString()
        {
            return Path;
        }
    }
}