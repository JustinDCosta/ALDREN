using S3.Voice.Pipeline;

namespace Aldren.Spikes.Tests;

public class WerTests
{
    [Fact]
    public void One_wrong_word_is_one_substitution()
    {
        Assert.Equal((1, 3), Wer.Count("open spotify now", "open spotify know"));
    }

    [Fact]
    public void Missing_and_extra_words_both_count()
    {
        Assert.Equal((2, 3), Wer.Count("set a timer", "set timer please"));
    }

    [Fact]
    public void Punctuation_and_case_are_ignored()
    {
        Assert.Equal((0, 4), Wer.Count("What's on my screen?", "what's on my screen"));
    }

    [Fact]
    public void Number_words_and_digits_count_as_the_same_word()
    {
        Assert.Equal((0, 6), Wer.Count("Set the volume to thirty percent.", "Set the volume to 30%."));
        Assert.Equal((0, 5), Wer.Count("Remind me at seven thirty.", "Remind me at 7:30."));
    }

    [Fact]
    public void French_numbers_and_percent_signs_count_as_the_same_word()
    {
        Assert.Equal((0, 6), Wer.Count("Mets le volume à cinquante pour cent.", "Mets le volume à 50 %."));
        Assert.Equal((0, 5), Wer.Count("du pain à dix-huit heures", "du pain à 18h"));
    }
}
