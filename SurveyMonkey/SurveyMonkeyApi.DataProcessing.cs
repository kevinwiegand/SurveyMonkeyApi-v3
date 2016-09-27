﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using SurveyMonkey.Containers;
using SurveyMonkey.Enums;
using SurveyMonkey.ProcessedAnswers;

namespace SurveyMonkey
{
    public partial class SurveyMonkeyApi
    {
        public List<Survey> PopulateMultipleSurveyResponseInformation(List<long> surveyIds)
        {
            var result = new List<Survey>();
            foreach (var surveyId in surveyIds)
            {
                result.Add(PopulateSurveyResponseInformation(surveyId));
            }
            return result;
        }

        public Survey PopulateSurveyResponseInformation(long surveyId)
        {
            Survey survey = GetSurveyDetails(surveyId);
            survey.Responses = GetResponseDetailList(surveyId, ObjectType.Survey);

            foreach (var response in survey.Responses)
            {
                MatchResponsesToSurveyStructure(survey, response);
            }

            return survey;
        }

        private void MatchResponsesToSurveyStructure(Survey survey, Response response)
        {
            foreach (var question in survey.Questions)
            {
                question.Answers?.PopulateItemLookup();
                if (question.Family == QuestionFamily.Demographic)
                {
                    question.Answers?.PopulateDemographicTypeLookup();
                }
                if (question.Family == QuestionFamily.Matrix && question.Subtype == QuestionSubtype.Menu)
                {
                    question.Answers?.PopulateColChoicesLookup();
                }
            }
            Dictionary<long, Question> questionsLookup = survey.Questions.Where(q => q.Id.HasValue).ToDictionary(q => q.Id.Value, q => q);
            MatchIndividualResponseToSurveyStructure(response, questionsLookup);
        }

        private void MatchIndividualResponseToSurveyStructure(Response response, Dictionary<long, Question> questionsLookup)
        {
            if (response.Questions == null) //In rare cases there can be a respondent with no responses to questions //todo is this still true?
            {
                return;
            }
            foreach (var responseQuestion in response.Questions)
            {
                //First try to match the ResponseQuestion with the survey structure
                if (questionsLookup.ContainsKey(responseQuestion.Id.Value))
                {
                    responseQuestion.ProcessedAnswer = new ProcessedAnswer
                    {
                        QuestionFamily = questionsLookup[responseQuestion.Id.Value].Family,
                        QuestionSubtype = questionsLookup[responseQuestion.Id.Value].Subtype,
                        Response = MatchResponseQuestionToSurveyStructure(questionsLookup[responseQuestion.Id.Value], responseQuestion.Answers)
                        //todo original version stored QuestionHeading
                    };
                }

                //todo Try to retrived from custom variables
                //todo Deleted questions?
                //If there's still no match (probably because the question's been deleted), leave ProcessedAnswer as null
            }
        }

        private IProcessedResponse MatchResponseQuestionToSurveyStructure(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            switch (question.Family)
            {
                case QuestionFamily.SingleChoice:
                    return MatchSingleChoiceAnswer(question, responseAnswers);

                case QuestionFamily.MultipleChoice:
                    return MatchMultipleChoiceAnswer(question, responseAnswers);

                case QuestionFamily.OpenEnded:
                    switch (question.Subtype)
                    {
                        case QuestionSubtype.Essay:
                        case QuestionSubtype.Single:
                            return MatchOpenEndedSingleAnswer(question, responseAnswers);

                        case QuestionSubtype.Multi:
                        case QuestionSubtype.Numerical:
                            return MatchOpenEndedMultipleAnswer(question, responseAnswers);
                    }
                    break;

                case QuestionFamily.Demographic:
                    return MatchDemographicAnswer(question, responseAnswers);

                case QuestionFamily.DateTime:
                    return MatchDateTimeAnswer(question, responseAnswers);

                case QuestionFamily.Matrix:
                    switch (question.Subtype)
                    {
                        case QuestionSubtype.Menu:
                            return MatchMatrixMenuAnswer(question, responseAnswers);
                        case QuestionSubtype.Ranking:
                            return MatchMatrixRankingAnswer(question, responseAnswers);
                        case QuestionSubtype.Rating:
                            return MatchMatrixRatingAnswer(question, responseAnswers);
                        case QuestionSubtype.Single:
                            return MatchMatrixSingleAnswer(question, responseAnswers);
                        case QuestionSubtype.Multi:
                            return MatchMatrixMultiAnswer(question, responseAnswers);
                    }
                    break;
            }
            return null;
        }

        private SingleChoiceAnswer MatchSingleChoiceAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new SingleChoiceAnswer();

            foreach (var responseAnswer in responseAnswers)
            {
                if (responseAnswer.OtherId.HasValue)
                {
                    reply.OtherText = responseAnswer.Text;
                }
                else if (responseAnswer.ChoiceId.HasValue)
                {
                    reply.Choice = question.Answers.ItemLookup[responseAnswer.ChoiceId.Value];
                }

            }
            return reply;
        }

        private MultipleChoiceAnswer MatchMultipleChoiceAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new MultipleChoiceAnswer
            {
                Choices = new List<string>()
            };

            foreach (var responseAnswer in responseAnswers)
            {
                if (responseAnswer.OtherId.HasValue)
                {
                    reply.OtherText = responseAnswer.Text;
                }
                if (responseAnswer.ChoiceId.HasValue && responseAnswer.ChoiceId != 0)
                {
                    reply.Choices.Add(question.Answers.ItemLookup[responseAnswer.ChoiceId.Value]);
                }
            }
            return reply;
        }

        private OpenEndedSingleAnswer MatchOpenEndedSingleAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new OpenEndedSingleAnswer
            {
                Text = responseAnswers.FirstOrDefault()?.Text
            };
            return reply;
        }

        private OpenEndedMultipleAnswer MatchOpenEndedMultipleAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new OpenEndedMultipleAnswer
            {
                Rows = new List<OpenEndedMultipleAnswerRow>()
            };

            foreach (var responseAnswer in responseAnswers)
            {
                if (responseAnswer.RowId.HasValue)
                {
                    reply.Rows.Add(new OpenEndedMultipleAnswerRow
                    {
                        RowName = question.Answers.ItemLookup[responseAnswer.RowId.Value],
                        Text = responseAnswer.Text
                    });
                }
            }

            return reply;
        }

        private DemographicAnswer MatchDemographicAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new DemographicAnswer();

            foreach (var responseAnswer in responseAnswers)
            {
                if (responseAnswer.RowId.HasValue)
                {
                    string propertyName = question.Answers.DemographicTypeLookup[responseAnswer.RowId.Value];

                    PropertyInfo property = typeof(DemographicAnswer).GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                    if (property != null)
                    {
                        property.SetValue(reply, responseAnswer.Text);
                    }
                }
            }
            return reply;
        }

        private DateTimeAnswer MatchDateTimeAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new DateTimeAnswer
            {
                Rows = new List<DateTimeAnswerRow>()
            };

            foreach (var responseAnswer in responseAnswers)
            {
                if (responseAnswer.RowId.HasValue)
                {
                    var dateTimeAnswerReply = new DateTimeAnswerRow
                    {
                        RowName = question.Answers.ItemLookup[responseAnswer.RowId.Value],
                        TimeStamp = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
                    };

                    DateTime timeStamp = DateTime.Parse(responseAnswer.Text, CultureInfo.CreateSpecificCulture("en-US"));
                    DateTime utcTimeStamp = DateTime.SpecifyKind(timeStamp, DateTimeKind.Utc);
                    if (question.Subtype == QuestionSubtype.TimeOnly) //Where only a time is given, use date component from DateTime.MinValue
                    {
                        dateTimeAnswerReply.TimeStamp = dateTimeAnswerReply.TimeStamp.AddHours(utcTimeStamp.Hour);
                        dateTimeAnswerReply.TimeStamp = dateTimeAnswerReply.TimeStamp.AddMinutes(utcTimeStamp.Minute);
                    }
                    else
                    {
                        dateTimeAnswerReply.TimeStamp = utcTimeStamp;
                    }
                    reply.Rows.Add(dateTimeAnswerReply);
                }
            }

            return reply;
        }

        private MatrixMenuAnswer MatchMatrixMenuAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            if (question.Answers.Cols.Any(c => c.LegacyChoices != null))
            {
                return null;
            }

            var reply = new MatrixMenuAnswer
            {
                Rows = new Dictionary<string, MatrixMenuAnswerRow>()
            };
            
            foreach (var responseAnswer in responseAnswers)
            {
                if (!String.IsNullOrEmpty(responseAnswer.Text))
                {
                    reply.OtherText = responseAnswer.Text;
                }
                else if (responseAnswer.ChoiceId.HasValue)
                {
                    if (!reply.Rows.ContainsKey(question.Answers.ItemLookup[responseAnswer.RowId.Value]))
                    {
                        reply.Rows.Add(question.Answers.ItemLookup[responseAnswer.RowId.Value], new MatrixMenuAnswerRow {Columns = new Dictionary<string, string>()});
                    }
                    reply.Rows[question.Answers.ItemLookup[responseAnswer.RowId.Value]].Columns.Add(question.Answers.ItemLookup[responseAnswer.ColId.Value], question.Answers.ColChoicesLookup[responseAnswer.ChoiceId.Value]);
                }
            }
            
            return reply;
        }

        private MatrixRankingAnswer MatchMatrixRankingAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new MatrixRankingAnswer
            {
                Ranking = new Dictionary<int, string>(),
                NotApplicable = new List<string>()
            };
            
            foreach (var responseAnswer in responseAnswers)
            {
                if (question.Answers.Choices.Any(c => c.IsNa.Value && c.Id.Value == responseAnswer.ChoiceId.Value))
                {
                    reply.NotApplicable.Add(question.Answers.Choices.FirstOrDefault(c => c.IsNa.Value && c.Id.Value == responseAnswer.ChoiceId.Value).Text);
                }
                else
                {
                    reply.Ranking.Add(Int32.Parse(question.Answers.ItemLookup[responseAnswer.ChoiceId.Value]), question.Answers.ItemLookup[responseAnswer.RowId.Value]);
                }
            }

            return reply;
        }

        private MatrixRatingAnswer MatchMatrixRatingAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new MatrixRatingAnswer
            {
                Rows = new List<MatrixRatingAnswerRow>()
            };

            var rowDictionary = new Dictionary<long, MatrixRatingAnswerRow>();

            foreach (var responseAnswer in responseAnswers)
            {
                if (!responseAnswer.RowId.HasValue)
                {
                    reply.OtherText = responseAnswer.Text;
                }
                else
                {
                    MatrixRatingAnswerRow row;
                    if (!rowDictionary.ContainsKey(responseAnswer.RowId.Value))
                    {
                        row = new MatrixRatingAnswerRow
                        {
                            RowName = question.Answers.ItemLookup[responseAnswer.RowId.Value]
                        };
                        rowDictionary.Add(responseAnswer.RowId.Value, row);
                    }
                    else
                    {
                        row = rowDictionary[responseAnswer.RowId.Value];
                    }

                    if (responseAnswer.ChoiceId.HasValue)
                    {
                        row.Choice = question.Answers.ItemLookup[responseAnswer.ChoiceId.Value];
                    }

                    if (responseAnswer.OtherId.HasValue)
                    {
                        row.OtherText = responseAnswer.Text;
                    }
                }
            }
            reply.Rows = rowDictionary.Values.ToList();

            return reply;
        }

        private MatrixSingleAnswer MatchMatrixSingleAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new MatrixSingleAnswer
            {
                Rows = new List<MatrixSingleAnswerRow>()
            };

            foreach (var responseAnswer in responseAnswers)
            {
                if (!responseAnswer.RowId.HasValue)
                {
                    reply.OtherText = responseAnswer.Text;
                }
                else
                {
                    reply.Rows.Add(new MatrixSingleAnswerRow
                    {
                        RowName = question.Answers.ItemLookup[responseAnswer.RowId.Value],
                        Choice = question.Answers.ItemLookup[responseAnswer.ChoiceId.Value]
                    });
                }
            }

            return reply;
        }

        private MatrixMultiAnswer MatchMatrixMultiAnswer(Question question, IEnumerable<ResponseAnswer> responseAnswers)
        {
            var reply = new MatrixMultiAnswer();

            var rowDictionary = new Dictionary<long, MatrixMultiAnswerRow>();

            foreach (var responseAnswer in responseAnswers)
            {
                if (!responseAnswer.RowId.HasValue)
                {
                    reply.OtherText = responseAnswer.Text;
                }
                else
                {
                    if (!rowDictionary.ContainsKey(responseAnswer.RowId.Value))
                    {
                        rowDictionary.Add(responseAnswer.RowId.Value, new MatrixMultiAnswerRow
                        {
                            RowName = question.Answers.ItemLookup[responseAnswer.RowId.Value],
                            Choices = new List<string>()
                        });
                    }
                    rowDictionary[responseAnswer.RowId.Value].Choices.Add(question.Answers.ItemLookup[responseAnswer.ChoiceId.Value]);
                }
            }

            reply.Rows = rowDictionary.Values.ToList();

            return reply;
        }
    }
}