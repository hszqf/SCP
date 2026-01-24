using System;
using System.Collections.Generic;
using System.Linq;

namespace Core
{
    public enum EventSource
    {
        Investigate,
        Contain,
        LocalPanicHigh,
        Fixed,
        SecuredManage,
        Random
    }

    [Serializable]
    public class EventEffect
    {
        public int LocalPanicDelta = 0;
        public int PopulationDelta = 0;
        public int GlobalPanicDelta = 0;
        public int MoneyDelta = 0;
        public int NegEntropyDelta = 0;

        public override string ToString()
        {
            return $"LocalPanicDelta={LocalPanicDelta},PopulationDelta={PopulationDelta},GlobalPanicDelta={GlobalPanicDelta},MoneyDelta={MoneyDelta},NegEntropyDelta={NegEntropyDelta}";
        }
    }

    [Serializable]
    public class EventOption
    {
        public string OptionId;
        public string Text;
        public string ResultText;
        public EventEffect Effects = new EventEffect();
    }

    [Serializable]
    public class EventInstance
    {
        public string EventId;
        public EventSource Source;
        public string NodeId;
        public string Title;
        public string Desc;
        public List<EventOption> Options = new();
        public int CreatedDay;
        public EventEffect IgnorePenalty = new EventEffect();
    }

    [Serializable]
    public class EventTemplate
    {
        public EventSource Source;
        public string Title;
        public string Desc;
        public List<EventOption> Options = new();
        public EventEffect IgnorePenalty = new EventEffect();
    }

    public static class EventTemplates
    {
        private static readonly List<EventTemplate> Templates = new()
        {
            BuildInvestigate(),
            BuildContain(),
            BuildLocalPanicHigh(),
            BuildFixed(),
            BuildSecuredManage(),
            BuildRandom(),
        };

        public static EventInstance CreateInstance(EventSource source, string nodeId, int day, Random rng)
        {
            var template = PickTemplate(source, rng);
            if (template == null) return null;

            return new EventInstance
            {
                EventId = $"EV_{Guid.NewGuid().ToString("N")[..8]}",
                Source = source,
                NodeId = nodeId,
                Title = template.Title,
                Desc = template.Desc,
                Options = template.Options.Select(CloneOption).ToList(),
                CreatedDay = day,
                IgnorePenalty = CloneEffect(template.IgnorePenalty)
            };
        }

        private static EventTemplate PickTemplate(EventSource source, Random rng)
        {
            var candidates = Templates.Where(t => t != null && t.Source == source).ToList();
            if (candidates.Count == 0) return null;
            if (rng == null || candidates.Count == 1) return candidates[0];
            return candidates[rng.Next(candidates.Count)];
        }

        private static EventOption CloneOption(EventOption opt)
        {
            if (opt == null) return null;
            return new EventOption
            {
                OptionId = opt.OptionId,
                Text = opt.Text,
                ResultText = opt.ResultText,
                Effects = CloneEffect(opt.Effects)
            };
        }

        private static EventEffect CloneEffect(EventEffect eff)
        {
            if (eff == null) return new EventEffect();
            return new EventEffect
            {
                LocalPanicDelta = eff.LocalPanicDelta,
                PopulationDelta = eff.PopulationDelta,
                GlobalPanicDelta = eff.GlobalPanicDelta,
                MoneyDelta = eff.MoneyDelta,
                NegEntropyDelta = eff.NegEntropyDelta
            };
        }

        private static EventTemplate BuildInvestigate()
        {
            return new EventTemplate
            {
                Source = EventSource.Investigate,
                Title = "调查引发关注",
                Desc = "调查行动被目击，媒体开始追问。",
                IgnorePenalty = new EventEffect { LocalPanicDelta = 2, PopulationDelta = -1 },
                Options = new List<EventOption>
                {
                    new EventOption
                    {
                        OptionId = "INV_CALM",
                        Text = "低调安抚目击者",
                        Effects = new EventEffect { LocalPanicDelta = -1, PopulationDelta = 0 }
                    },
                    new EventOption
                    {
                        OptionId = "INV_FORCE",
                        Text = "强硬封锁消息",
                        Effects = new EventEffect { LocalPanicDelta = -2, PopulationDelta = -1, GlobalPanicDelta = 1 }
                    }
                }
            };
        }

        private static EventTemplate BuildContain()
        {
            return new EventTemplate
            {
                Source = EventSource.Contain,
                Title = "收容现场余波",
                Desc = "收容过程造成附带损失，需要后续处理。",
                IgnorePenalty = new EventEffect { LocalPanicDelta = 2, PopulationDelta = -1 },
                Options = new List<EventOption>
                {
                    new EventOption
                    {
                        OptionId = "CON_REPAIR",
                        Text = "优先修复与善后",
                        Effects = new EventEffect { LocalPanicDelta = -1, MoneyDelta = -50 }
                    },
                    new EventOption
                    {
                        OptionId = "CON_PUSH",
                        Text = "压下损失继续推进",
                        Effects = new EventEffect { LocalPanicDelta = 1, PopulationDelta = -1, MoneyDelta = 50 }
                    }
                }
            };
        }

        private static EventTemplate BuildLocalPanicHigh()
        {
            return new EventTemplate
            {
                Source = EventSource.LocalPanicHigh,
                Title = "本地恐慌蔓延",
                Desc = "恐慌已接近失控，社区秩序开始崩坏。",
                IgnorePenalty = new EventEffect { LocalPanicDelta = 3, PopulationDelta = -1, GlobalPanicDelta = 1 },
                Options = new List<EventOption>
                {
                    new EventOption
                    {
                        OptionId = "LP_PUBLIC",
                        Text = "公开安抚与物资投放",
                        Effects = new EventEffect { LocalPanicDelta = -2, MoneyDelta = -80 }
                    },
                    new EventOption
                    {
                        OptionId = "LP_LOCKDOWN",
                        Text = "强制管制区域",
                        Effects = new EventEffect { LocalPanicDelta = -1, PopulationDelta = -1 }
                    }
                }
            };
        }

        private static EventTemplate BuildFixed()
        {
            return new EventTemplate
            {
                Source = EventSource.Fixed,
                Title = "预定演习事故",
                Desc = "例行演习中发生误报，公众开始质疑机构能力。",
                IgnorePenalty = new EventEffect { LocalPanicDelta = 2, GlobalPanicDelta = 1 },
                Options = new List<EventOption>
                {
                    new EventOption
                    {
                        OptionId = "FX_APOLOGY",
                        Text = "发布正式说明",
                        Effects = new EventEffect { LocalPanicDelta = -1, MoneyDelta = -30 }
                    },
                    new EventOption
                    {
                        OptionId = "FX_SPIN",
                        Text = "转移舆论焦点",
                        Effects = new EventEffect { LocalPanicDelta = 0, GlobalPanicDelta = 1, MoneyDelta = 30 }
                    }
                }
            };
        }

        private static EventTemplate BuildSecuredManage()
        {
            return new EventTemplate
            {
                Source = EventSource.SecuredManage,
                Title = "管理流程审计",
                Desc = "已收容异常的管理流程被抽查，需要即时回应。",
                IgnorePenalty = new EventEffect { LocalPanicDelta = 1, NegEntropyDelta = -1 },
                Options = new List<EventOption>
                {
                    new EventOption
                    {
                        OptionId = "SM_COMPLY",
                        Text = "全面配合审计",
                        Effects = new EventEffect { LocalPanicDelta = -1, NegEntropyDelta = -1 }
                    },
                    new EventOption
                    {
                        OptionId = "SM_HIDE",
                        Text = "压缩审计范围",
                        Effects = new EventEffect { LocalPanicDelta = 1, NegEntropyDelta = 1 }
                    }
                }
            };
        }

        private static EventTemplate BuildRandom()
        {
            return new EventTemplate
            {
                Source = EventSource.Random,
                Title = "偶发目击报告",
                Desc = "匿名举报称发现异常活动，需要选择响应策略。",
                IgnorePenalty = new EventEffect { LocalPanicDelta = 1 },
                Options = new List<EventOption>
                {
                    new EventOption
                    {
                        OptionId = "RD_VERIFY",
                        Text = "谨慎核查",
                        Effects = new EventEffect { LocalPanicDelta = -1, MoneyDelta = -20 }
                    },
                    new EventOption
                    {
                        OptionId = "RD_SWEEP",
                        Text = "快速清场",
                        Effects = new EventEffect { LocalPanicDelta = 1, PopulationDelta = -1 }
                    }
                }
            };
        }
    }
}
